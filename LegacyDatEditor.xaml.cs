using Efundies;
using Google.Protobuf;
using Google.Protobuf.Collections;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tibia.Protobuf.Appearances;
using WinForms = System.Windows.Forms;

namespace Assets_Editor
{
    /// <summary>
    /// Interaction logic for LegacyDatEditor.xaml
    /// </summary>
    public partial class LegacyDatEditor : Window
    {
        public ObservableCollection<ShowList> ThingsOutfit = new ObservableCollection<ShowList>();
        public ObservableCollection<ShowList> ThingsItem = new ObservableCollection<ShowList>();
        public ObservableCollection<ShowList> ThingsEffect = new ObservableCollection<ShowList>();
        public ObservableCollection<ShowList> ThingsMissile = new ObservableCollection<ShowList>();
        public Appearance CurrentObjectAppearance;
        public Appearance ReplaceObjectAppearance;
        public AppearanceFlags CurrentFlags = null;
        private int CurrentSprDir = 2;
        private bool isPageLoaded = false;
        private bool isObjectLoaded = false;
        private bool _isSwitchingAssets;

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (!_isSwitchingAssets)
            {
                Application.Current.Shutdown();
            }
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            isPageLoaded = true;
        }

        private bool IsTextInputFocused()
        {
            var element = FocusManager.GetFocusedElement(this) as DependencyObject;
            if (element == null)
            {
                return false;
            }

            return element is TextBoxBase
                || element is PasswordBox
                || (element is ComboBox combo && combo.IsEditable);
        }

        private void CopyFlagsCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (CopyFlagsFromCurrent())
            {
                e.Handled = true;
            }
        }

        private void PasteFlagsCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (PasteFlagsOntoCurrent())
            {
                e.Handled = true;
            }
        }

        private void SaveCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SaveCurrentObject();
            e.Handled = true;
        }

        private void ClearFlagsCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ClearCurrentFlags();
            e.Handled = true;
        }

        private void DuplicateCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            DuplicateCurrentSelection();
            e.Handled = true;
        }

        private void FlagClipboardCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = CurrentObjectAppearance != null && !IsTextInputFocused();
        }

        private void PasteFlagsCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = CurrentObjectAppearance != null && FlagClipboard.HasFlags && !IsTextInputFocused();
        }

        private void SaveCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = CurrentObjectAppearance != null;
        }

        private void DuplicateCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ObjListView?.SelectedItem != null && CurrentObjectAppearance != null;
        }

        private void OpenAssetsCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ExecuteOpenAssetsWorkflow();
        }

        private void ExecuteOpenAssetsWorkflow()
        {
            using WinForms.FolderBrowserDialog dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select the assets folder to open"
            };

            if (dialog.ShowDialog() != WinForms.DialogResult.OK)
            {
                return;
            }

            OpenAssetsFromPath(dialog.SelectedPath);
        }

        private void OpenAssetsFromPath(string selectedPath)
        {
            var folderKind = AssetReloadService.DetectFolderKind(selectedPath);
            if (folderKind == AssetReloadService.AssetFolderKind.Unknown)
            {
                MessageBox.Show("Select a folder that contains catalog-content.json or Tibia.dat/Tibia.spr files.", "Assets Editor", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AssetReloadService.LegacyAssetOptions legacyOptions = null;
            if (folderKind == AssetReloadService.AssetFolderKind.Legacy)
            {
                LegacyAssetsOptionsDialog optionsDialog = new LegacyAssetsOptionsDialog
                {
                    Owner = this
                };

                if (optionsDialog.ShowDialog() != true)
                {
                    return;
                }

                legacyOptions = new AssetReloadService.LegacyAssetOptions
                {
                    Version = optionsDialog.SelectedVersion,
                    Transparent = optionsDialog.UseTransparency
                };
            }

            AssetReloadService.AssetPreviewInfo previewInfo;
            try
            {
                previewInfo = AssetReloadService.GetPreview(selectedPath, legacyOptions);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível inspecionar o client: {ex.Message}", "Assets Editor", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!ConfirmAssetPreview(previewInfo))
            {
                return;
            }

            try
            {
                AssetReloadService.AssetFolderKind resultKind = AssetReloadService.Reload(selectedPath, legacyOptions);
                if (resultKind == AssetReloadService.AssetFolderKind.Legacy)
                {
                    ApplyLegacyAssetsReload();
                }
                else
                {
                    _isSwitchingAssets = true;
                    DatEditor datEditor = new DatEditor(MainWindow.appearances);
                    datEditor.Show();
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open assets: {ex.Message}", "Assets Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyLegacyAssetsReload()
        {
            if (MainWindow.appearances == null)
            {
                MessageBox.Show("No appearances were loaded from the selected assets.", "Assets Editor", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            void SyncCollection(ObservableCollection<ShowList> target, IEnumerable<Appearance> source)
            {
                target.Clear();
                foreach (var appearance in source)
                {
                    target.Add(new ShowList { Id = appearance.Id });
                }
            }

            CurrentObjectAppearance = null;
            SyncCollection(ThingsOutfit, MainWindow.appearances.Outfit);
            SyncCollection(ThingsItem, MainWindow.appearances.Object);
            SyncCollection(ThingsEffect, MainWindow.appearances.Effect);
            SyncCollection(ThingsMissile, MainWindow.appearances.Missile);

            SprListView.ItemsSource = MainWindow.AllSprList;
            CollectionViewSource.GetDefaultView(SprListView.ItemsSource)?.Refresh();
            ObjListView?.UnselectAll();
            UpdateShowList(ObjectMenu.SelectedIndex);

            StatusBar.MessageQueue.Enqueue($"Loaded assets from {MainWindow._assetsPath}", null, null, null, false, true, TimeSpan.FromSeconds(2));
        }
        private bool ConfirmAssetPreview(AssetReloadService.AssetPreviewInfo previewInfo)
        {
            if (previewInfo == null)
            {
                return false;
            }

            AssetPreviewDialog dialog = new AssetPreviewDialog(previewInfo)
            {
                Owner = this
            };
            return dialog.ShowDialog() == true;
        }

        public LegacyDatEditor()
        {
            InitializeComponent();
            A_FlagAutomapColorPicker.AvailableColors.Clear();
            for (int x = 0; x <= 215; x++)
            {
                Color myRgbColor = Utils.Get8Bit(x);
                A_FlagAutomapColorPicker.AvailableColors.Add(new Xceed.Wpf.Toolkit.ColorItem(System.Windows.Media.Color.FromRgb(myRgbColor.R, myRgbColor.G, myRgbColor.B), x.ToString()));
            }
            ObservableCollection<Xceed.Wpf.Toolkit.ColorItem> outfitColors = new ObservableCollection<Xceed.Wpf.Toolkit.ColorItem>();
            SprLayerHeadPicker.AvailableColors = outfitColors;
            SprLayerHeadPicker.AvailableColors.Clear();

            for (int x = 0; x <= 132; x++)
            {
                System.Drawing.Color myRgbColor = Utils.GetOutfitColor(x);
                outfitColors.Add(new Xceed.Wpf.Toolkit.ColorItem(System.Windows.Media.Color.FromRgb(myRgbColor.R, myRgbColor.G, myRgbColor.B), x.ToString()));
            }
            SprLayerHeadPicker.AvailableColors = outfitColors;
            SprLayerBodyPicker.AvailableColors = outfitColors;
            SprLayerLegsPicker.AvailableColors = outfitColors;
            SprLayerFeetPicker.AvailableColors = outfitColors;
        }
        private void DarkModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            PaletteHelper palette = new PaletteHelper();

            ITheme theme = palette.GetTheme();
            if ((bool)DarkModeToggle.IsChecked)
            {
                theme.SetBaseTheme(Theme.Dark);
            }
            else
            {
                theme.SetBaseTheme(Theme.Light);
            }
            palette.SetTheme(theme);
        }
        public LegacyDatEditor(Appearances appearances)
            : this()
        {
            foreach (var outfit in appearances.Outfit)
            {
                ThingsOutfit.Add(new ShowList() { Id = outfit.Id });
            }
            foreach (var item in appearances.Object)
            {
                ThingsItem.Add(new ShowList() { Id = item.Id });
            }
            foreach (var effect in appearances.Effect)
            {
                ThingsEffect.Add(new ShowList() { Id = effect.Id });
            }
            foreach (var missile in appearances.Missile)
            {
                ThingsMissile.Add(new ShowList() { Id = missile.Id });
            }
            SprListView.ItemsSource = MainWindow.AllSprList;
            UpdateShowList(ObjectMenu.SelectedIndex);
        }
        public void UpdateShowList(int selection, uint? preserveId = null)
        {
            if (ObjListView != null)
            {
                ObjListViewSelectedIndex.Minimum = 1;
                ObservableCollection<ShowList> itemsSource = selection switch
                {
                    0 => ThingsOutfit,
                    1 => ThingsItem,
                    2 => ThingsEffect,
                    3 => ThingsMissile,
                    _ => null
                };

                if (selection == 1)
                {
                    ObjListViewSelectedIndex.Minimum = 100;
                }

                if (itemsSource == null)
                {
                    return;
                }

                ObjListView.ItemsSource = itemsSource;

                if (preserveId.HasValue)
                {
                    var match = itemsSource.FirstOrDefault(item => item.Id == preserveId.Value);
                    if (match != null)
                    {
                        ObjListView.SelectedItem = match;
                        ObjListView.ScrollIntoView(match);
                        return;
                    }
                }

                ObjListView.SelectedIndex = 0;
            }
        }
        private void SprListView_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            VirtualizingStackPanel panel = Utils.FindVisualChild<VirtualizingStackPanel>(SprListView);
            if (SprListView.Items.Count > 0 && panel != null)
            {
                int offset = (int)panel.VerticalOffset;
                for (int i = 0; i < SprListView.Items.Count; i++)
                {
                    if (i >= offset && i < Math.Min(offset + 20, SprListView.Items.Count) && MainWindow.SprLists.ContainsKey(i))
                        MainWindow.AllSprList[i].Image = Utils.BitmapToBitmapImage(MainWindow.MainSprStorage.getSpriteStream((uint)i));
                    else
                        MainWindow.AllSprList[i].Image = null;
                }
            }
        }
        private void SprListView_DragSpr(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                ShowList data = (ShowList)SprListView.SelectedItem;
                if (data != null && data.Image != null)
                {
                    SprListView.SelectedItem = data;
                    DragDrop.DoDragDrop(SprListView, data, DragDropEffects.Copy);
                }
            }
        }

        private void CancelSpriteDrag()
        {
            SprListView?.ReleaseMouseCapture();
            Mouse.Capture(null);
        }

        private static IEnumerable<Appearance> EnumerateAllAppearances()
        {
            foreach (var appearance in MainWindow.appearances.Outfit)
                yield return appearance;
            foreach (var appearance in MainWindow.appearances.Object)
                yield return appearance;
            foreach (var appearance in MainWindow.appearances.Effect)
                yield return appearance;
            foreach (var appearance in MainWindow.appearances.Missile)
                yield return appearance;
        }

        private static bool SpriteIdIsInUse(uint spriteId)
        {
            foreach (var appearance in EnumerateAllAppearances())
            {
                foreach (var frameGroup in appearance.FrameGroup)
                {
                    if (frameGroup.SpriteInfo.SpriteId.Contains(spriteId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int GetHighestSpriteId()
        {
            return MainWindow.SprLists.Keys.DefaultIfEmpty(-1).Max();
        }

        private static bool OverwriteSpriteWithTransparent(int spriteId)
        {
            if (!MainWindow.SprLists.TryGetValue(spriteId, out var stream))
            {
                return false;
            }

            lock (stream)
            {
                stream.SetLength(0);
                stream.Position = 0;
                using var emptyBitmap = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = System.Drawing.Graphics.FromImage(emptyBitmap))
                {
                    graphics.Clear(System.Drawing.Color.Transparent);
                }

                emptyBitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
            }

            if (spriteId >= 0 && spriteId < MainWindow.AllSprList.Count)
            {
                MainWindow.AllSprList[spriteId].Image = Utils.BitmapToBitmapImage(MainWindow.getSpriteStream(spriteId));
            }

            return true;
        }

        private static bool RemoveSpriteEntry(int spriteId)
        {
            if (spriteId < 0)
            {
                return false;
            }

            if (MainWindow.SprLists.TryRemove(spriteId, out var removedStream))
            {
                removedStream.Dispose();
            }

            if (spriteId >= 0 && spriteId < MainWindow.AllSprList.Count)
            {
                MainWindow.AllSprList.RemoveAt(spriteId);
            }

            return true;
        }

        private void RefreshSpriteListView()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CollectionViewSource.GetDefaultView(SprListView.ItemsSource)?.Refresh();
            }), DispatcherPriority.Background);
        }

        private static string BuildSpriteDeletionMessage(int removed, int reset, int skipped)
        {
            List<string> parts = new();
            if (removed > 0)
            {
                parts.Add($"{removed} removed");
            }
            if (reset > 0)
            {
                parts.Add($"{reset} reset");
            }
            if (skipped > 0)
            {
                parts.Add($"{skipped} in-use");
            }

            return parts.Count == 0 ? "No sprites modified." : $"Sprites processed: {string.Join(", ", parts)}.";
        }
        private void SprListViewSelectedIndex_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (SprListView.IsLoaded && e.NewValue != null)
            {
                int nIndex = (int)e.NewValue;
                SprListView.SelectedIndex = nIndex;
                ScrollViewer scrollViewer = Utils.FindVisualChild<ScrollViewer>(SprListView);
                VirtualizingStackPanel panel = Utils.FindVisualChild<VirtualizingStackPanel>(SprListView);
                int offset = (int)panel.VerticalOffset;
                int maxOffset = (int)panel.ViewportHeight;
                if (nIndex - maxOffset == offset)
                    scrollViewer.ScrollToVerticalOffset(offset + 1);
                else if (nIndex + 1 == offset)
                    scrollViewer.ScrollToVerticalOffset(offset - 1);
                else if (nIndex >= offset + maxOffset || nIndex < offset)
                    scrollViewer.ScrollToVerticalOffset(SprListView.SelectedIndex);
            }
        }

        private void ObjectMenuChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateShowList(ObjectMenu.SelectedIndex);
        }
        private void ObjListView_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            VirtualizingStackPanel panel = Utils.FindVisualChild<VirtualizingStackPanel>(ObjListView);
            if (ObjListView.Items.Count > 0 && panel != null)
            {
                int offset = (int)panel.VerticalOffset;
                for (int i = 0; i < ObjListView.Items.Count; i++)
                {
                    Appearance appearance = null;
                    ShowList item = (ShowList)ObjListView.Items[i];
                    if (i >= offset && i < Math.Min(offset + 20, ObjListView.Items.Count))
                    {
                        if (ObjectMenu.SelectedIndex == 0)
                            appearance = MainWindow.appearances.Outfit[i];
                        else if (ObjectMenu.SelectedIndex == 1)
                            appearance = MainWindow.appearances.Object[i];
                        else if (ObjectMenu.SelectedIndex == 2)
                            appearance = MainWindow.appearances.Effect[i];
                        else if (ObjectMenu.SelectedIndex == 3)
                            appearance = MainWindow.appearances.Missile[i];

                        AnimateSelectedListItem(item);
                    }
                    else
                    {
                        item.StopAnimation();
                        if (ObjectMenu.SelectedIndex == 0)
                            ThingsOutfit[i].Image = null;
                        else if (ObjectMenu.SelectedIndex == 1)
                            ThingsItem[i].Image = null;
                        else if (ObjectMenu.SelectedIndex == 2)
                            ThingsEffect[i].Image = null;
                        else if (ObjectMenu.SelectedIndex == 3)
                            ThingsMissile[i].Image = null;
                    }
                }
            }
        }

        private void ObjListViewSelectedIndex_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ObjListView.IsLoaded)
            {
                foreach (ShowList item in ObjListView.Items)
                {
                    if (item.Id == ObjListViewSelectedIndex.Value)
                    {
                        ObjListView.SelectedItem = item;
                        ScrollViewer scrollViewer = Utils.FindVisualChild<ScrollViewer>(ObjListView);
                        VirtualizingStackPanel panel = Utils.FindVisualChild<VirtualizingStackPanel>(ObjListView);
                        int offset = (int)panel.VerticalOffset;
                        int maxOffset = (int)panel.ViewportHeight;
                        if (ObjListView.SelectedIndex > offset + maxOffset || ObjListView.SelectedIndex < offset)
                        {
                            scrollViewer.ScrollToVerticalOffset(ObjListView.SelectedIndex);
                        }
                        break;
                    }
                }
            }

        }
        private void ObjListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ShowList showList = (ShowList)ObjListView.SelectedItem;
            if (showList != null)
            {
                ObjListViewSelectedIndex.Value = (int)showList.Id;
                if (ObjectMenu.SelectedIndex == 0)
                    LoadSelectedObjectAppearances(MainWindow.appearances.Outfit[ObjListView.SelectedIndex]);
                else if (ObjectMenu.SelectedIndex == 1)
                    LoadSelectedObjectAppearances(MainWindow.appearances.Object[ObjListView.SelectedIndex]);
                else if (ObjectMenu.SelectedIndex == 2)
                    LoadSelectedObjectAppearances(MainWindow.appearances.Effect[ObjListView.SelectedIndex]);
                else if (ObjectMenu.SelectedIndex == 3)
                    LoadSelectedObjectAppearances(MainWindow.appearances.Missile[ObjListView.SelectedIndex]);

                if (ObjectMenu.SelectedIndex == 0)
                {
                    SprUpArrow.Visibility = Visibility.Visible;
                    SprDownArrow.Visibility = Visibility.Visible;
                    SprLeftArrow.Visibility = Visibility.Visible;
                    SprRightArrow.Visibility = Visibility.Visible;
                    SprAddonSlider.IsEnabled = true;
                    SprGroupSlider.IsEnabled = true;
                    A_SprGroups.IsEnabled = true;
                }
                else
                {
                    SprUpArrow.Visibility = Visibility.Hidden;
                    SprDownArrow.Visibility = Visibility.Hidden;
                    SprLeftArrow.Visibility = Visibility.Hidden;
                    SprRightArrow.Visibility = Visibility.Hidden;
                    SprAddonSlider.IsEnabled = false;
                    SprGroupSlider.IsEnabled = false;
                    A_SprGroups.IsEnabled = false;
                }

            }
        }
        private void LoadSelectedObjectAppearances(Appearance ObjectAppearance)
        {
            CurrentObjectAppearance = ObjectAppearance.Clone();
            LoadCurrentObjectAppearances();
            SprGroupSlider.ValueChanged -= SprGroupSlider_ValueChanged;
            SprGroupSlider.Value = 0;
            ChangeGroupType(0);
            SprGroupSlider.ValueChanged += SprGroupSlider_ValueChanged;
        }

        private void ChangeGroupType(int group)
        {
            isObjectLoaded = false;
            A_SprGroups.Value = CurrentObjectAppearance.FrameGroup.Count;
            A_SprWidth.Value = (int)CurrentObjectAppearance.FrameGroup[group].SpriteInfo.PatternWidth;
            A_SprHeight.Value = (int)CurrentObjectAppearance.FrameGroup[group].SpriteInfo.PatternHeight;
            A_SprSize.Value = (int)CurrentObjectAppearance.FrameGroup[group].SpriteInfo.PatternSize;
            A_SprLayers.Value = (int)CurrentObjectAppearance.FrameGroup[group].SpriteInfo.PatternLayers;
            A_SprPaternX.Value = (int)CurrentObjectAppearance.FrameGroup[group].SpriteInfo.PatternX;
            A_SprPaternY.Value = (int)CurrentObjectAppearance.FrameGroup[group].SpriteInfo.PatternY;
            A_SprPaternZ.Value = (int)CurrentObjectAppearance.FrameGroup[group].SpriteInfo.PatternZ;
            A_SprAnimation.Value = (int)CurrentObjectAppearance.FrameGroup[group].SpriteInfo.PatternFrames;
            CurrentSprDir = 2;
            ButtonProgressAssist.SetIsIndicatorVisible(SprUpArrow, false);
            ButtonProgressAssist.SetIsIndicatorVisible(SprRightArrow, false);
            ButtonProgressAssist.SetIsIndicatorVisible(SprDownArrow, true);
            ButtonProgressAssist.SetIsIndicatorVisible(SprLeftArrow, false);
            ForceSliderChange();
            SprFramesSlider.Maximum = (double)A_SprAnimation.Value - 1;
            if (A_SprAnimation.Value > 1)
            {
                SprDefaultPhase.IsEnabled = true;
                SprSynchronized.IsEnabled = true;
                SprPhaseMin.IsEnabled = true;
                SprPhaseMax.IsEnabled = true;
                SprLoopCount.IsEnabled = true;

                SprDefaultPhase.Value = (int?)CurrentObjectAppearance.FrameGroup[group].SpriteInfo.Animation.DefaultStartPhase;
                SprSynchronized.IsChecked = CurrentObjectAppearance.FrameGroup[group].SpriteInfo.Animation.AnimationMode == ANIMATION_ANIMATION_MODE.AnimationSynchronized ? true : false;
                SprPhaseMin.Value = (int?)CurrentObjectAppearance.FrameGroup[group].SpriteInfo.Animation.SpritePhase[0].DurationMin;
                SprPhaseMax.Value = (int?)CurrentObjectAppearance.FrameGroup[group].SpriteInfo.Animation.SpritePhase[0].DurationMax;
                SprLoopCount.Value = (int?)CurrentObjectAppearance.FrameGroup[group].SpriteInfo.Animation.LoopCount;
            }
            else
            {
                SprDefaultPhase.IsEnabled = false;
                SprSynchronized.IsEnabled = false;
                SprPhaseMin.Value = 0;
                SprPhaseMax.Value = 0;
                SprPhaseMin.IsEnabled = false;
                SprPhaseMax.IsEnabled = false;
                SprLoopCount.IsEnabled = false;
            }

            SprGroupType.Content = SprGroupSlider.Value == 0 ? "Idle" : "Walking";
            isObjectLoaded = true;
        }

        private void ForceSliderChange()
        {
            SprFramesSlider.ValueChanged -= SprFramesSlider_ValueChanged;
            SprFramesSlider.Minimum = -1;
            SprFramesSlider.Value = -1;
            SprFramesSlider.ValueChanged += SprFramesSlider_ValueChanged;
            SprFramesSlider.Minimum = 0;
        }

        private void LoadCurrentObjectAppearances()
        {
            A_FlagGround.IsChecked = CurrentObjectAppearance.Flags.Bank != null;
            A_FlagGroundSpeed.Value = (CurrentObjectAppearance.Flags.Bank != null) ? (int)CurrentObjectAppearance.Flags.Bank.Waypoints : 0;
            A_FlagClip.IsChecked = CurrentObjectAppearance.Flags.Clip;
            A_FlagBottom.IsChecked = CurrentObjectAppearance.Flags.Bottom;
            A_FlagTop.IsChecked = CurrentObjectAppearance.Flags.Top;
            A_FlagContainer.IsChecked = CurrentObjectAppearance.Flags.Container;
            A_FlagCumulative.IsChecked = CurrentObjectAppearance.Flags.Cumulative;
            A_FlagUsable.IsChecked = CurrentObjectAppearance.Flags.Usable;
            A_FlagForceuse.IsChecked = CurrentObjectAppearance.Flags.Forceuse;
            A_FlagMultiuse.IsChecked = CurrentObjectAppearance.Flags.Multiuse;
            A_FlagWrite.IsChecked = CurrentObjectAppearance.Flags.Write != null;
            A_FlagMaxTextLength.Value = (CurrentObjectAppearance.Flags.Write != null && CurrentObjectAppearance.Flags.Write.HasMaxTextLength) ? (int)CurrentObjectAppearance.Flags.Write.MaxTextLength : 0;
            A_FlagWriteOnce.IsChecked = CurrentObjectAppearance.Flags.WriteOnce != null;
            A_FlagMaxTextLengthOnce.Value = (CurrentObjectAppearance.Flags.WriteOnce != null && CurrentObjectAppearance.Flags.WriteOnce.HasMaxTextLengthOnce) ? (int)CurrentObjectAppearance.Flags.WriteOnce.MaxTextLengthOnce : 0;
            A_FlagLiquidpool.IsChecked = CurrentObjectAppearance.Flags.HasLiquidpool;
            A_FlagUnpass.IsChecked = CurrentObjectAppearance.Flags.HasUnpass;
            A_FlagUnmove.IsChecked = CurrentObjectAppearance.Flags.HasUnmove;
            A_FlagUnsight.IsChecked = CurrentObjectAppearance.Flags.HasUnsight;
            A_FlagAvoid.IsChecked = CurrentObjectAppearance.Flags.HasAvoid;
            A_FlagNoMoveAnimation.IsChecked = CurrentObjectAppearance.Flags.HasNoMovementAnimation;
            A_FlagTake.IsChecked = CurrentObjectAppearance.Flags.HasTake;
            A_FlagLiquidcontainer.IsChecked = CurrentObjectAppearance.Flags.HasLiquidcontainer;
            A_FlagHang.IsChecked = CurrentObjectAppearance.Flags.HasHang;
            A_FlagHookSouth.IsChecked = CurrentObjectAppearance.Flags.HookSouth;
            A_FlagHookEast.IsChecked = CurrentObjectAppearance.Flags.HookEast;
            A_FlagRotate.IsChecked = CurrentObjectAppearance.Flags.HasRotate;
            A_FlagLight.IsChecked = CurrentObjectAppearance.Flags.Light != null;
            A_FlagLightBrightness.Value = (CurrentObjectAppearance.Flags.Light != null && CurrentObjectAppearance.Flags.Light.HasBrightness) ? (int)CurrentObjectAppearance.Flags.Light.Brightness : 0;
            A_FlagLightColor.Value = (CurrentObjectAppearance.Flags.Light != null && CurrentObjectAppearance.Flags.Light.HasColor) ? (int)CurrentObjectAppearance.Flags.Light.Color : 0;
            A_FlagDontHide.IsChecked = CurrentObjectAppearance.Flags.HasDontHide;
            A_FlagTranslucent.IsChecked = CurrentObjectAppearance.Flags.HasTranslucent;
            A_FlagShift.IsChecked = CurrentObjectAppearance.Flags.Shift != null;
            A_FlagShiftX.Value = (CurrentObjectAppearance.Flags.Shift != null && CurrentObjectAppearance.Flags.Shift.HasX) ? (int)CurrentObjectAppearance.Flags.Shift.X : 0;
            A_FlagShiftY.Value = (CurrentObjectAppearance.Flags.Shift != null && CurrentObjectAppearance.Flags.Shift.HasY) ? (int)CurrentObjectAppearance.Flags.Shift.Y : 0;
            A_FlagHeight.IsChecked = CurrentObjectAppearance.Flags.Height != null;
            A_FlagElevation.Value = (CurrentObjectAppearance.Flags.Height != null && CurrentObjectAppearance.Flags.Height.HasElevation) ? (int)CurrentObjectAppearance.Flags.Height.Elevation : 0;
            A_FlagLyingObject.IsChecked = CurrentObjectAppearance.Flags.HasLyingObject;
            A_FlagAnimateAlways.IsChecked = CurrentObjectAppearance.Flags.HasAnimateAlways;
            A_FlagAutomap.IsChecked = CurrentObjectAppearance.Flags.Automap != null;
            A_FlagAutomapColor.Value = (CurrentObjectAppearance.Flags.Automap != null && CurrentObjectAppearance.Flags.Automap.HasColor) ? (int)CurrentObjectAppearance.Flags.Automap.Color : 0;
            A_FlagLenshelp.IsChecked = CurrentObjectAppearance.Flags.Lenshelp != null;
            A_FlagLenshelpId.SelectedIndex = (CurrentObjectAppearance.Flags.Lenshelp != null && CurrentObjectAppearance.Flags.Lenshelp.HasId) ? (int)CurrentObjectAppearance.Flags.Lenshelp.Id - 1100 : -1;
            A_FlagFullGround.IsChecked = CurrentObjectAppearance.Flags.HasFullbank;
            A_FlagIgnoreLook.IsChecked = CurrentObjectAppearance.Flags.HasIgnoreLook;
            A_FlagClothes.IsChecked = (CurrentObjectAppearance.Flags.Clothes != null && CurrentObjectAppearance.Flags.Clothes.HasSlot) ? true : false;
            A_FlagClothesSlot.SelectedIndex = (CurrentObjectAppearance.Flags.Clothes != null && CurrentObjectAppearance.Flags.Clothes.HasSlot) ? (int)CurrentObjectAppearance.Flags.Clothes.Slot : -1;
            A_FlagDefaultAction.IsChecked = CurrentObjectAppearance.Flags.DefaultAction != null;
            A_FlagDefaultActionType.SelectedIndex = (CurrentObjectAppearance.Flags.DefaultAction != null && CurrentObjectAppearance.Flags.DefaultAction.HasAction) ? (int)CurrentObjectAppearance.Flags.DefaultAction.Action : -1;
            A_FlagMarket.IsChecked = CurrentObjectAppearance.Flags.Market != null;
            A_FlagMarketCategory.SelectedIndex = (CurrentObjectAppearance.Flags.Market != null && CurrentObjectAppearance.Flags.Market.HasCategory) ? (int)CurrentObjectAppearance.Flags.Market.Category - 1 : -1;
            A_FlagMarketTrade.Value = (CurrentObjectAppearance.Flags.Market != null && CurrentObjectAppearance.Flags.Market.HasTradeAsObjectId) ? (int)CurrentObjectAppearance.Flags.Market.TradeAsObjectId : 0;
            A_FlagMarketShow.Value = (CurrentObjectAppearance.Flags.Market != null && CurrentObjectAppearance.Flags.Market.HasShowAsObjectId) ? (int)CurrentObjectAppearance.Flags.Market.ShowAsObjectId : 0;
            A_FlagMarketProfession.SelectedIndex = 0;
            if (CurrentObjectAppearance.Flags.Market != null && CurrentObjectAppearance.Flags.Market.HasVocation)
            {
                A_FlagMarketProfession.SelectedIndex = (int)CurrentObjectAppearance.Flags.Market.Vocation;
            }
            A_FlagMarketlevel.Value = (CurrentObjectAppearance.Flags.Market != null && CurrentObjectAppearance.Flags.Market.HasMinimumLevel) ? (int)CurrentObjectAppearance.Flags.Market.MinimumLevel : 0;
            A_FlagName.Text = CurrentObjectAppearance.HasName ? CurrentObjectAppearance.Name : null;
            A_FlagDescription.Text = CurrentObjectAppearance.HasDescription ? CurrentObjectAppearance.Description : null;
            A_FlagWrap.IsChecked = CurrentObjectAppearance.Flags.HasWrap;
            A_FlagUnwrap.IsChecked = CurrentObjectAppearance.Flags.HasUnwrap;
            A_FlagTopeffect.IsChecked = CurrentObjectAppearance.Flags.HasTop;

            A_FullInfo.Text = CurrentObjectAppearance.ToString();

            SynchronizeMarketIdsWithCurrentAppearance();
        }

        private void SynchronizeMarketIdsWithCurrentAppearance()
        {
            if (CurrentObjectAppearance == null)
            {
                return;
            }

            uint id = CurrentObjectAppearance.Id;
            if (A_FlagMarket.IsChecked == true)
            {
                A_FlagMarketTrade.Value = (int)id;
                A_FlagMarketShow.Value = (int)id;
            }
        }

        private void A_FlagMarket_Checked(object sender, RoutedEventArgs e)
        {
            SynchronizeMarketIdsWithCurrentAppearance();
        }
        private void A_FlagLightColorPickerChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            foreach (var color in A_FlagLightColorPicker.AvailableColors)
            {
                if (color.Color.Value.ToString() == A_FlagLightColorPicker.SelectedColor.ToString())
                    A_FlagLightColor.Value = int.Parse(color.Name);
            }

        }
        private void A_FlagAutomapColorPickerChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            foreach (var color in A_FlagAutomapColorPicker.AvailableColors)
            {
                if (color.Color.Value.ToString() == A_FlagAutomapColorPicker.SelectedColor.ToString())
                    A_FlagAutomapColor.Value = int.Parse(color.Name);
            }
        }
        private void A_FlagAutomapColor_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            A_FlagAutomapColorPicker.SelectedColor = A_FlagAutomapColorPicker.AvailableColors[(int)A_FlagAutomapColor.Value].Color;
        }
        private void A_FlagLightColor_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            A_FlagLightColorPicker.SelectedColor = A_FlagLightColorPicker.AvailableColors[(int)A_FlagLightColor.Value].Color;
        }
        private void A_FlagMarketProfession_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            A_FlagMarketProfession.SelectedIndex = -1;
        }
        private void Randomize_Click(object sender, RoutedEventArgs e)
        {
            Random rnd = new Random();
            SprLayerHeadPicker.SelectedColor = SprLayerHeadPicker.AvailableColors[rnd.Next(0, SprLayerHeadPicker.AvailableColors.Count)].Color;
            SprLayerBodyPicker.SelectedColor = SprLayerBodyPicker.AvailableColors[rnd.Next(0, SprLayerBodyPicker.AvailableColors.Count)].Color;
            SprLayerLegsPicker.SelectedColor = SprLayerLegsPicker.AvailableColors[rnd.Next(0, SprLayerLegsPicker.AvailableColors.Count)].Color;
            SprLayerFeetPicker.SelectedColor = SprLayerFeetPicker.AvailableColors[rnd.Next(0, SprLayerFeetPicker.AvailableColors.Count)].Color;
        }

        protected void Colorize(System.Drawing.Bitmap imageTemplate, System.Drawing.Bitmap imageOutfit, Color head, Color body, Color legs, Color feet)
        {
            for (int i = 0; i < imageTemplate.Height; i++)
            {
                for (int j = 0; j < imageTemplate.Width; j++)
                {
                    System.Drawing.Color templatePixel = imageTemplate.GetPixel(j, i);
                    System.Drawing.Color outfitPixel = imageOutfit.GetPixel(j, i);

                    if (templatePixel == outfitPixel)
                        continue;

                    int rt = templatePixel.R;
                    int gt = templatePixel.G;
                    int bt = templatePixel.B;
                    int ro = outfitPixel.R;
                    int go = outfitPixel.G;
                    int bo = outfitPixel.B;

                    if (rt > 0 && gt > 0 && bt == 0) // yellow == head
                    {
                        ColorizePixel(ref ro, ref go, ref bo, head);
                    }
                    else if (rt > 0 && gt == 0 && bt == 0) // red == body
                    {
                        ColorizePixel(ref ro, ref go, ref bo, body);
                    }
                    else if (rt == 0 && gt > 0 && bt == 0) // green == legs
                    {
                        ColorizePixel(ref ro, ref go, ref bo, legs);
                    }
                    else if (rt == 0 && gt == 0 && bt > 0) // blue == feet
                    {
                        ColorizePixel(ref ro, ref go, ref bo, feet);
                    }
                    else
                    {
                        continue; // if nothing changed, skip the change of pixel
                    }

                    imageOutfit.SetPixel(j, i, System.Drawing.Color.FromArgb(ro, go, bo));
                }
            }
        }

        private bool TryGetActiveCollections(out RepeatedField<Appearance> appearanceCollection, out ObservableCollection<ShowList> showListCollection)
        {
            appearanceCollection = null;
            showListCollection = null;

            switch (ObjectMenu.SelectedIndex)
            {
                case 0:
                    appearanceCollection = MainWindow.appearances.Outfit;
                    showListCollection = ThingsOutfit;
                    break;
                case 1:
                    appearanceCollection = MainWindow.appearances.Object;
                    showListCollection = ThingsItem;
                    break;
                case 2:
                    appearanceCollection = MainWindow.appearances.Effect;
                    showListCollection = ThingsEffect;
                    break;
                case 3:
                    appearanceCollection = MainWindow.appearances.Missile;
                    showListCollection = ThingsMissile;
                    break;
            }

            return appearanceCollection != null && showListCollection != null;
        }

        private uint GetMinimumIdForSelection(int selection) => selection == 1 ? 100u : 1u;

        private uint GenerateNextId(RepeatedField<Appearance> collection, int selection)
        {
            uint minId = GetMinimumIdForSelection(selection);
            if (collection == null || collection.Count == 0)
            {
                return minId;
            }

            uint next = (uint)collection.Max(a => (long)a.Id) + 1;
            if (selection == 1 && next < 100)
            {
                next = 100;
            }

            return next;
        }

        private void SortCollections(int selection)
        {
            switch (selection)
            {
                case 0:
                    SortAppearanceCollection(MainWindow.appearances.Outfit, ThingsOutfit);
                    break;
                case 1:
                    SortAppearanceCollection(MainWindow.appearances.Object, ThingsItem);
                    break;
                case 2:
                    SortAppearanceCollection(MainWindow.appearances.Effect, ThingsEffect);
                    break;
                case 3:
                    SortAppearanceCollection(MainWindow.appearances.Missile, ThingsMissile);
                    break;
            }
        }

        private static void SortAppearanceCollection(RepeatedField<Appearance> appearances, ObservableCollection<ShowList> showListCollection)
        {
            if (appearances == null || showListCollection == null)
            {
                return;
            }

            var orderedAppearances = appearances.OrderBy(item => item.Id).ToList();
            appearances.Clear();
            foreach (var appearance in orderedAppearances)
            {
                appearances.Add(appearance);
            }

            var lookup = showListCollection.ToDictionary(show => show.Id);
            var orderedShowLists = new List<ShowList>(orderedAppearances.Count);
            foreach (var appearance in orderedAppearances)
            {
                if (!lookup.TryGetValue(appearance.Id, out var show))
                {
                    show = new ShowList { Id = appearance.Id };
                }
                else
                {
                    show.Id = appearance.Id;
                }

                orderedShowLists.Add(show);
            }

            showListCollection.Clear();
            foreach (var show in orderedShowLists)
            {
                showListCollection.Add(show);
            }
        }

        private uint? DuplicateAppearance(ShowList sourceItem)
        {
            if (sourceItem == null)
            {
                return null;
            }

            if (!TryGetActiveCollections(out var appearanceCollection, out var showListCollection))
            {
                return null;
            }

            var sourceAppearance = appearanceCollection.FirstOrDefault(a => a.Id == sourceItem.Id);
            if (sourceAppearance == null)
            {
                return null;
            }

            var duplicate = sourceAppearance.Clone();
            duplicate.Id = GenerateNextId(appearanceCollection, ObjectMenu.SelectedIndex);
            AppearanceSyncHelper.SyncMarketAndCyclopedia(duplicate);

            appearanceCollection.Add(duplicate);
            showListCollection.Add(new ShowList { Id = duplicate.Id });
            SortCollections(ObjectMenu.SelectedIndex);

            return duplicate.Id;
        }

        private void DuplicateCurrentSelection()
        {
            if (ObjListView?.SelectedItem is not ShowList selectedItem)
            {
                return;
            }

            var newId = DuplicateAppearance(selectedItem);
            if (newId.HasValue)
            {
                UpdateShowList(ObjectMenu.SelectedIndex);
                ObjListViewSelectedIndex.Value = (int)newId.Value;
                StatusBar.MessageQueue.Enqueue($"Duplicated object as {newId.Value}.", null, null, null, false, true, TimeSpan.FromSeconds(2));
            }
        }

        protected void ColorizePixel(ref int r, ref int g, ref int b, Color colorPart)
        {
            r = (r + colorPart.R) / 2;
            g = (g + colorPart.G) / 2;
            b = (b + colorPart.B) / 2;
        }
        private void SprFramesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            FrameGroup frameGroup = CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value];
            if (frameGroup.SpriteInfo.PatternFrames > 1)
            {
                SprPhaseMin.Value = (int)frameGroup.SpriteInfo.Animation.SpritePhase[(int)SprFramesSlider.Value].DurationMin;
                SprPhaseMax.Value = (int)frameGroup.SpriteInfo.Animation.SpritePhase[(int)SprFramesSlider.Value].DurationMax;
            }

            SpriteViewerGrid.Children.Clear();
            SpriteViewerGrid.RowDefinitions.Clear();
            SpriteViewerGrid.ColumnDefinitions.Clear();
            int gridWidth = 1;
            int gridHeight = 1;
            if (CurrentObjectAppearance.AppearanceType == APPEARANCE_TYPE.AppearanceOutfit)
            {
                gridWidth = (int)frameGroup.SpriteInfo.PatternHeight;
                gridHeight = (int)frameGroup.SpriteInfo.PatternWidth;
            }
            else
            {
                gridWidth = (int)(frameGroup.SpriteInfo.PatternHeight * frameGroup.SpriteInfo.PatternY);
                gridHeight = (int)(frameGroup.SpriteInfo.PatternWidth * frameGroup.SpriteInfo.PatternX);
            }
            for (int i = 0; i < gridWidth; i++)
            {
                RowDefinition rowDef = new RowDefinition();
                rowDef.Height = new GridLength(32);
                SpriteViewerGrid.RowDefinitions.Add(rowDef);
            }
            for (int i = 0; i < gridHeight; i++)
            {
                ColumnDefinition colDef = new ColumnDefinition();
                colDef.Width = new GridLength(32);
                SpriteViewerGrid.ColumnDefinitions.Add(colDef);
            }

            if (CurrentObjectAppearance.AppearanceType == APPEARANCE_TYPE.AppearanceOutfit)
            {
                if ((bool)SprBlendLayers.IsChecked == false)
                {
                    int counter = 1;
                    int layer = SprBlendLayer.IsChecked == true ? (int)frameGroup.SpriteInfo.PatternLayers - 1 : 0;
                    int mount = SprMount.IsChecked == true ? (int)frameGroup.SpriteInfo.PatternZ - 1 : 0;
                    int addon = frameGroup.SpriteInfo.PatternY > 1 ? (int)SprAddonSlider.Value : 0;
                    for (int h = (int)frameGroup.SpriteInfo.PatternHeight - 1; h >= 0; h--)
                    {
                        for (int w = (int)frameGroup.SpriteInfo.PatternWidth - 1; w >= 0; w--)
                        {
                            int index = LegacyAppearance.GetSpriteIndex(frameGroup, w, h, layer, (int)Math.Min(CurrentSprDir, frameGroup.SpriteInfo.PatternX - 1), addon, mount, (int)SprFramesSlider.Value);
                            int spriteId = (int)frameGroup.SpriteInfo.SpriteId[index];
                            SetImageInGrid(SpriteViewerGrid, gridHeight, Utils.BitmapToBitmapImage(MainWindow.MainSprStorage.getSpriteStream((uint)spriteId)), counter, spriteId, index);
                            counter++;
                        }
                    }
                }
                else
                {
                    int counter = 1;
                    for (int h = (int)frameGroup.SpriteInfo.PatternHeight - 1; h >= 0; h--)
                    {
                        for (int w = (int)frameGroup.SpriteInfo.PatternWidth - 1; w >= 0; w--)
                        {
                            int baseIndex = LegacyAppearance.GetSpriteIndex(frameGroup, w, h, 0, (int)Math.Min(CurrentSprDir, frameGroup.SpriteInfo.PatternX - 1), 0, 0, (int)SprFramesSlider.Value);
                            int baseSpriteId = (int)frameGroup.SpriteInfo.SpriteId[baseIndex];
                            System.Drawing.Bitmap baseBitmap = new System.Drawing.Bitmap(MainWindow.MainSprStorage.getSpriteStream((uint)baseSpriteId));
                            if (frameGroup.SpriteInfo.PatternLayers > 1)
                            {
                                int baseLayerIndex = LegacyAppearance.GetSpriteIndex(frameGroup, w, h, 1, (int)Math.Min(CurrentSprDir, frameGroup.SpriteInfo.PatternX - 1), 0, 0, (int)SprFramesSlider.Value);
                                int baseLayerSpriteId = (int)frameGroup.SpriteInfo.SpriteId[baseLayerIndex];
                                System.Drawing.Bitmap baseLayerBitmap = new System.Drawing.Bitmap(MainWindow.MainSprStorage.getSpriteStream((uint)baseLayerSpriteId));
                                Colorize(baseLayerBitmap, baseBitmap, SprLayerHeadPicker.SelectedColor.Value, SprLayerBodyPicker.SelectedColor.Value, SprLayerLegsPicker.SelectedColor.Value, SprLayerFeetPicker.SelectedColor.Value);
                            }
                            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(baseBitmap))
                            {
                                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;

                                if ((bool)SprFullAddons.IsChecked)
                                {
                                    for (int x = 1; x <= (int)SprAddonSlider.Maximum; x++)
                                    {
                                        int addonIndex = LegacyAppearance.GetSpriteIndex(frameGroup, w, h, 0, (int)Math.Min(CurrentSprDir, frameGroup.SpriteInfo.PatternX - 1), x, 0, (int)SprFramesSlider.Value);
                                        int addonSpriteId = (int)frameGroup.SpriteInfo.SpriteId[addonIndex];
                                        System.Drawing.Bitmap addonBitmap = new System.Drawing.Bitmap(MainWindow.MainSprStorage.getSpriteStream((uint)addonSpriteId));

                                        int addonLayerIndex = LegacyAppearance.GetSpriteIndex(frameGroup, w, h, 1, (int)Math.Min(CurrentSprDir, frameGroup.SpriteInfo.PatternX - 1), x, 0, (int)SprFramesSlider.Value);
                                        int addonLayerSpriteId = (int)frameGroup.SpriteInfo.SpriteId[addonLayerIndex];
                                        System.Drawing.Bitmap addonLayerBitmap = new System.Drawing.Bitmap(MainWindow.MainSprStorage.getSpriteStream((uint)addonLayerSpriteId));

                                        Colorize(addonLayerBitmap, addonBitmap, SprLayerHeadPicker.SelectedColor.Value, SprLayerBodyPicker.SelectedColor.Value, SprLayerLegsPicker.SelectedColor.Value, SprLayerFeetPicker.SelectedColor.Value);
                                        g.DrawImage(addonBitmap, new System.Drawing.Point(0, 0));

                                    }
                                }
                            }



                            MemoryStream memoryStream = new MemoryStream();
                            baseBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                            SetImageInGrid(SpriteViewerGrid, gridHeight, Utils.BitmapToBitmapImage(memoryStream), counter, 0, 0);
                            counter++;
                        }
                    }

                }
            }
            else
            {
                int counter = 1;
                int mount = SprMount.IsChecked == true ? (int)frameGroup.SpriteInfo.PatternZ - 1 : 0;
                for (int ph = 0; ph < frameGroup.SpriteInfo.PatternY; ph++)
                {
                    for (int pw = 0; pw < frameGroup.SpriteInfo.PatternX; pw++)
                    {
                        for (int h = (int)(frameGroup.SpriteInfo.PatternHeight - 1); h >= 0; h--)
                        {
                            for (int w = (int)(frameGroup.SpriteInfo.PatternWidth - 1); w >= 0; w--)
                            {
                                int tileid = (int)(ph * gridHeight * frameGroup.SpriteInfo.PatternHeight + (frameGroup.SpriteInfo.PatternHeight - 1 - h) * gridHeight + (pw * frameGroup.SpriteInfo.PatternWidth) + (frameGroup.SpriteInfo.PatternWidth - 1 - w) + 1);
                                int index = LegacyAppearance.GetSpriteIndex(frameGroup, w, h, 0, pw, ph, mount, (int)SprFramesSlider.Value);
                                int spriteId = (int)frameGroup.SpriteInfo.SpriteId[index];
                                SetImageInGrid(SpriteViewerGrid, gridHeight, Utils.BitmapToBitmapImage(MainWindow.MainSprStorage.getSpriteStream((uint)spriteId)), tileid, spriteId, index);
                                counter++;
                            }
                        }
                    }
                }
            }

        }
        private void SprOutfitChanged(object sender, RoutedEventArgs e)
        {
            ForceSliderChange();
        }

        private void SprLayerPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            if (isPageLoaded)
                ForceSliderChange();
        }
        private void OutfitXml(object sender, RoutedEventArgs e)
        {
            int typeValue = (int)CurrentObjectAppearance.Id;
            int headValue = 0;
            int bodyValue = 0;
            int legsValue = 0;
            int feetValue = 0;
            int corpseValue = 0;

            foreach (var color in SprLayerHeadPicker.AvailableColors)
            {
                if (color.Color.Value.ToString() == SprLayerHeadPicker.SelectedColor.ToString())
                    headValue = int.Parse(color.Name);
                if (color.Color.Value.ToString() == SprLayerBodyPicker.SelectedColor.ToString())
                    bodyValue = int.Parse(color.Name);
                if (color.Color.Value.ToString() == SprLayerLegsPicker.SelectedColor.ToString())
                    legsValue = int.Parse(color.Name);
                if (color.Color.Value.ToString() == SprLayerFeetPicker.SelectedColor.ToString())
                    feetValue = int.Parse(color.Name);
            }

            string xml = $"<look type=\"{typeValue}\" head=\"{headValue}\" body=\"{bodyValue}\" legs=\"{legsValue}\" feet=\"{feetValue}\" corpse=\"{corpseValue}\"/>";
            Clipboard.SetText(xml);
            StatusBar.MessageQueue.Enqueue($"xml copied to clipboard.", null, null, null, false, true, TimeSpan.FromSeconds(2));
        }
        private void SetImageInGrid(Grid grid, int gridHeight, BitmapImage image, int id, int spriteId, int index)
        {
            // Get the row and column of the cell based on the ID number
            int row = (id - 1) / gridHeight;
            int col = (id - 1) % gridHeight;
            // Get the existing Image control in the cell, or create a new one if it doesn't exist
            Image existingImage = null;
            foreach (UIElement element in grid.Children)
            {
                if (Grid.GetRow(element) == row && Grid.GetColumn(element) == col && element is Image)
                {
                    existingImage = element as Image;
                    break;
                }
            }
            if (existingImage == null)
            {
                existingImage = new Image();
                existingImage.Width = 32;
                existingImage.Height = 32;
                AllowDrop = true;
                Grid.SetRow(existingImage, row);
                Grid.SetColumn(existingImage, col);
                grid.Children.Add(existingImage);
            }
            existingImage.MouseLeftButtonDown += Img_PreviewMouseLeftButtonDown;
            existingImage.Drop += Spr_Drop;
            existingImage.ToolTip = spriteId.ToString();
            existingImage.Tag = index;
            // Set the Source property of the Image control to the specified Image
            existingImage.Source = image;
            RenderOptions.SetBitmapScalingMode(existingImage, BitmapScalingMode.NearestNeighbor);
        }

        private void SprGroupSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ChangeGroupType((int)SprGroupSlider.Value);
        }


        private void ChangeDirection(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Button _dir = (Button)sender;

            CurrentSprDir = int.Parse(_dir.Uid);
            ForceSliderChange();
            ButtonProgressAssist.SetIsIndicatorVisible(SprUpArrow, false);
            ButtonProgressAssist.SetIsIndicatorVisible(SprRightArrow, false);
            ButtonProgressAssist.SetIsIndicatorVisible(SprDownArrow, false);
            ButtonProgressAssist.SetIsIndicatorVisible(SprLeftArrow, false);
            ButtonProgressAssist.SetIsIndicatorVisible(_dir, true);
        }


        private void Img_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Image img = e.Source as Image;
            SprListView.SelectedIndex = int.Parse((string)img.ToolTip);
            ScrollViewer scrollViewer = Utils.FindVisualChild<ScrollViewer>(SprListView);
            scrollViewer.ScrollToVerticalOffset(SprListView.SelectedIndex);
        }
        private void Spr_Drop(object sender, DragEventArgs e)
        {
            Image img = e.Source as Image;
            ShowList data = (ShowList)e.Data.GetData(typeof(ShowList));
            img.Source = data.Image;
            img.ToolTip = data.Id.ToString();
            CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo.SpriteId[(int)img.Tag] = uint.Parse((string)img.ToolTip);
        }

        private void SprMount_Click(object sender, RoutedEventArgs e)
        {
            ForceSliderChange();
        }

        private void SprBlendLayer_Click(object sender, RoutedEventArgs e)
        {
            ForceSliderChange();
        }

        private void SprAddonSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ForceSliderChange();
        }

        private void FixSpritesCount()
        {
            SpriteInfo spriteInfo = CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo;
            int NumSprites = (int)(spriteInfo.PatternWidth * spriteInfo.PatternHeight * spriteInfo.PatternLayers * spriteInfo.PatternX * spriteInfo.PatternY * spriteInfo.PatternZ * spriteInfo.PatternFrames);

            if (spriteInfo.SpriteId.Count > NumSprites)
            {
                // Remove excess sprites from spriteInfo.SpriteId until it's equal to NumSprites
                int excessCount = spriteInfo.SpriteId.Count - NumSprites;
                for (int i = 0; i < excessCount; i++)
                {
                    spriteInfo.SpriteId.RemoveAt(spriteInfo.SpriteId.Count - 1);
                }
            }
            else if (spriteInfo.SpriteId.Count < NumSprites)
            {
                // Add child id 0 to spriteInfo.SpriteId until it's equal to NumSprites
                int missingCount = NumSprites - spriteInfo.SpriteId.Count;
                for (int i = 0; i < missingCount; i++)
                {
                    spriteInfo.SpriteId.Add(0);
                }
            }
        }

        private void A_Texture_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!isPageLoaded || isObjectLoaded == false)
                return;

            FrameworkElement frameworkElement = sender as FrameworkElement;
            SpriteInfo spriteInfo = CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo;
            if (frameworkElement.Name == "A_SprGroups")
            {
                if (A_SprGroups.Value == 1 && CurrentObjectAppearance.FrameGroup.Count == 2)
                {
                    CurrentObjectAppearance.FrameGroup.RemoveAt(1);
                    CurrentObjectAppearance.FrameGroup[0].FixedFrameGroup = FIXED_FRAME_GROUP.OutfitMoving;
                }
                else if (A_SprGroups.Value == 2 && CurrentObjectAppearance.FrameGroup.Count == 1)
                {
                    FrameGroup newFrameGroup = CurrentObjectAppearance.FrameGroup[0].Clone();
                    CurrentObjectAppearance.FrameGroup.Add(newFrameGroup);
                    CurrentObjectAppearance.FrameGroup[0].FixedFrameGroup = FIXED_FRAME_GROUP.OutfitIdle;
                }
            }
            else if (frameworkElement.Name == "A_SprWidth")
            {
                spriteInfo.PatternWidth = (uint)A_SprWidth.Value;
                FixSpritesCount();
                ForceSliderChange();
            }
            else if (frameworkElement.Name == "A_SprHeight")
            {
                spriteInfo.PatternHeight = (uint)A_SprHeight.Value;
                FixSpritesCount();
                ForceSliderChange();
            }
            else if (frameworkElement.Name == "A_SprLayers")
            {
                spriteInfo.PatternLayers = (uint)A_SprLayers.Value;
                FixSpritesCount();
                ForceSliderChange();
                SprBlendLayer.IsEnabled = A_SprLayers.Value > 1 ? true : false;
            }
            else if (frameworkElement.Name == "A_SprPaternX")
            {
                spriteInfo.PatternX = (uint)A_SprPaternX.Value;
                FixSpritesCount();
                ForceSliderChange();
            }
            else if (frameworkElement.Name == "A_SprPaternY")
            {
                spriteInfo.PatternY = (uint)A_SprPaternY.Value;
                FixSpritesCount();
                ForceSliderChange();
            }
            else if (frameworkElement.Name == "A_SprPaternZ")
            {
                spriteInfo.PatternZ = (uint)A_SprPaternZ.Value;
                FixSpritesCount();
                ForceSliderChange();
                SprMount.IsEnabled = A_SprPaternZ.Value > 1 ? true : false;
            }
            else if (frameworkElement.Name == "A_SprAnimation")
            {
                spriteInfo.PatternFrames = (uint)A_SprAnimation.Value;
                if (spriteInfo.PatternFrames == 1)
                {
                    spriteInfo.Animation = null;
                }
                else
                {
                    SpriteAnimation spriteAnimation = new SpriteAnimation();
                    for (int i = 0; i < spriteInfo.PatternFrames; i++)
                        spriteAnimation.SpritePhase.Add(new SpritePhase() { DurationMin = 100, DurationMax = 100 });
                    spriteInfo.Animation = spriteAnimation;
                }

                FixSpritesCount();
                ForceSliderChange();
                SpriteAnimationGroup.IsEnabled = A_SprAnimation.Value > 1 ? true : false;
                SprFramesSlider.Maximum = (double)A_SprAnimation.Value - 1;
            }
        }

        private void SaveCurrentObject()
        {
            if (!string.IsNullOrWhiteSpace(A_FlagName.Text))
                CurrentObjectAppearance.Name = A_FlagName.Text;

            if (!string.IsNullOrWhiteSpace(A_FlagDescription.Text))
                CurrentObjectAppearance.Description = A_FlagDescription.Text;

            if ((bool)A_FlagGround.IsChecked)
            {
                CurrentObjectAppearance.Flags.Bank = new AppearanceFlagBank
                {
                    Waypoints = (uint)A_FlagGroundSpeed.Value
                };
            }
            else
                CurrentObjectAppearance.Flags.Bank = null;

            if ((bool)A_FlagClip.IsChecked)
                CurrentObjectAppearance.Flags.Clip = true;
            else if (CurrentObjectAppearance.Flags.HasClip)
                CurrentObjectAppearance.Flags.ClearClip();

            if ((bool)A_FlagBottom.IsChecked)
                CurrentObjectAppearance.Flags.Bottom = true;
            else if (CurrentObjectAppearance.Flags.HasBottom)
                CurrentObjectAppearance.Flags.ClearBottom();

            if ((bool)A_FlagTop.IsChecked)
                CurrentObjectAppearance.Flags.Top = true;
            else if (CurrentObjectAppearance.Flags.HasTop)
                CurrentObjectAppearance.Flags.ClearTop();

            if ((bool)A_FlagContainer.IsChecked)
                CurrentObjectAppearance.Flags.Container = true;
            else if (CurrentObjectAppearance.Flags.HasContainer)
                CurrentObjectAppearance.Flags.ClearContainer();

            if ((bool)A_FlagCumulative.IsChecked)
                CurrentObjectAppearance.Flags.Cumulative = true;
            else if (CurrentObjectAppearance.Flags.HasCumulative)
                CurrentObjectAppearance.Flags.ClearCumulative();

            if ((bool)A_FlagUsable.IsChecked)
                CurrentObjectAppearance.Flags.Usable = true;
            else if (CurrentObjectAppearance.Flags.HasUsable)
                CurrentObjectAppearance.Flags.ClearUsable();

            if ((bool)A_FlagForceuse.IsChecked)
                CurrentObjectAppearance.Flags.Forceuse = true;
            else if (CurrentObjectAppearance.Flags.HasForceuse)
                CurrentObjectAppearance.Flags.ClearForceuse();

            if ((bool)A_FlagMultiuse.IsChecked)
                CurrentObjectAppearance.Flags.Multiuse = true;
            else if (CurrentObjectAppearance.Flags.HasMultiuse)
                CurrentObjectAppearance.Flags.ClearMultiuse();

            if ((bool)A_FlagWrite.IsChecked)
            {
                CurrentObjectAppearance.Flags.Write = new AppearanceFlagWrite
                {
                    MaxTextLength = (uint)A_FlagMaxTextLength.Value
                };
            }
            else
                CurrentObjectAppearance.Flags.Write = null;

            if ((bool)A_FlagWriteOnce.IsChecked)
            {
                CurrentObjectAppearance.Flags.WriteOnce = new AppearanceFlagWriteOnce
                {
                    MaxTextLengthOnce = (uint)A_FlagMaxTextLengthOnce.Value
                };
            }
            else CurrentObjectAppearance.Flags.WriteOnce = null;

            if ((bool)A_FlagLiquidpool.IsChecked)
                CurrentObjectAppearance.Flags.Liquidpool = true;
            else if (CurrentObjectAppearance.Flags.HasLiquidpool)
                CurrentObjectAppearance.Flags.ClearLiquidpool();

            if ((bool)A_FlagUnpass.IsChecked)
                CurrentObjectAppearance.Flags.Unpass = true;
            else if (CurrentObjectAppearance.Flags.HasUnpass)
                CurrentObjectAppearance.Flags.ClearUnpass();

            if ((bool)A_FlagUnmove.IsChecked)
                CurrentObjectAppearance.Flags.Unmove = true;
            else if (CurrentObjectAppearance.Flags.HasUnmove)
                CurrentObjectAppearance.Flags.ClearUnmove();

            if ((bool)A_FlagUnsight.IsChecked)
                CurrentObjectAppearance.Flags.Unsight = true;
            else if (CurrentObjectAppearance.Flags.HasUnsight)
                CurrentObjectAppearance.Flags.ClearUnsight();

            if ((bool)A_FlagAvoid.IsChecked)
                CurrentObjectAppearance.Flags.Avoid = true;
            else if (CurrentObjectAppearance.Flags.HasAvoid)
                CurrentObjectAppearance.Flags.ClearAvoid();

            if ((bool)A_FlagNoMoveAnimation.IsChecked)
                CurrentObjectAppearance.Flags.NoMovementAnimation = true;

            else if (CurrentObjectAppearance.Flags.HasNoMovementAnimation)
                CurrentObjectAppearance.Flags.ClearNoMovementAnimation();

            if ((bool)A_FlagTake.IsChecked)
                CurrentObjectAppearance.Flags.Take = true;
            else if (CurrentObjectAppearance.Flags.HasTake)
                CurrentObjectAppearance.Flags.ClearTake();

            if ((bool)A_FlagLiquidcontainer.IsChecked)
                CurrentObjectAppearance.Flags.Liquidcontainer = true;
            else if (CurrentObjectAppearance.Flags.HasLiquidcontainer)
                CurrentObjectAppearance.Flags.ClearLiquidcontainer();

            if ((bool)A_FlagHang.IsChecked)
                CurrentObjectAppearance.Flags.Hang = true;
            else if (CurrentObjectAppearance.Flags.HasHang)
                CurrentObjectAppearance.Flags.ClearHang();

            if ((bool)A_FlagHookSouth.IsChecked)
                CurrentObjectAppearance.Flags.HookSouth = true;
            else if (CurrentObjectAppearance.Flags.HasHookSouth)
                CurrentObjectAppearance.Flags.ClearHookSouth();

            if ((bool)A_FlagHookEast.IsChecked)
                CurrentObjectAppearance.Flags.HookEast = true;
            else if (CurrentObjectAppearance.Flags.HasHookEast)
                CurrentObjectAppearance.Flags.ClearHookEast();

            if ((bool)A_FlagRotate.IsChecked)
                CurrentObjectAppearance.Flags.Rotate = true;
            else if (CurrentObjectAppearance.Flags.HasRotate)
                CurrentObjectAppearance.Flags.ClearRotate();

            if ((bool)A_FlagLight.IsChecked)
            {
                CurrentObjectAppearance.Flags.Light = new AppearanceFlagLight
                {
                    Brightness = (uint)A_FlagLightBrightness.Value,
                    Color = (uint)A_FlagLightColor.Value
                };
            }
            else
                CurrentObjectAppearance.Flags.Light = null;


            if ((bool)A_FlagDontHide.IsChecked)
                CurrentObjectAppearance.Flags.DontHide = true;
            else if (CurrentObjectAppearance.Flags.HasDontHide)
                CurrentObjectAppearance.Flags.ClearDontHide();

            if ((bool)A_FlagTranslucent.IsChecked)
                CurrentObjectAppearance.Flags.Translucent = true;
            else if (CurrentObjectAppearance.Flags.HasTranslucent)
                CurrentObjectAppearance.Flags.ClearTranslucent();

            if ((bool)A_FlagShift.IsChecked)
            {
                CurrentObjectAppearance.Flags.Shift = new AppearanceFlagShift
                {
                    X = (uint)A_FlagShiftX.Value,
                    Y = (uint)A_FlagShiftY.Value
                };
            }
            else
                CurrentObjectAppearance.Flags.Shift = null;

            if ((bool)A_FlagHeight.IsChecked)
            {
                CurrentObjectAppearance.Flags.Height = new AppearanceFlagHeight
                {
                    Elevation = (uint)A_FlagElevation.Value,
                };
            }
            else CurrentObjectAppearance.Flags.Height = null;

            if ((bool)A_FlagLyingObject.IsChecked)
                CurrentObjectAppearance.Flags.LyingObject = true;
            else if (CurrentObjectAppearance.Flags.HasLyingObject)
                CurrentObjectAppearance.Flags.ClearLyingObject();

            if ((bool)A_FlagAnimateAlways.IsChecked)
                CurrentObjectAppearance.Flags.AnimateAlways = true;
            else if (CurrentObjectAppearance.Flags.HasAnimateAlways)
                CurrentObjectAppearance.Flags.ClearAnimateAlways();

            if ((bool)A_FlagAutomap.IsChecked)
            {
                CurrentObjectAppearance.Flags.Automap = new AppearanceFlagAutomap
                {
                    Color = (uint)A_FlagAutomapColor.Value,
                };
            }
            else CurrentObjectAppearance.Flags.Automap = null;

            if ((bool)A_FlagLenshelp.IsChecked)
            {
                CurrentObjectAppearance.Flags.Lenshelp = new AppearanceFlagLenshelp
                {
                    Id = (uint)A_FlagLenshelpId.SelectedIndex + 1100
                };
            }
            else CurrentObjectAppearance.Flags.Lenshelp = null;

            if ((bool)A_FlagFullGround.IsChecked)
                CurrentObjectAppearance.Flags.Fullbank = true;
            else if (CurrentObjectAppearance.Flags.HasFullbank)
                CurrentObjectAppearance.Flags.ClearFullbank();

            if ((bool)A_FlagIgnoreLook.IsChecked)
                CurrentObjectAppearance.Flags.IgnoreLook = true;
            else if (CurrentObjectAppearance.Flags.HasIgnoreLook)
                CurrentObjectAppearance.Flags.ClearIgnoreLook();

            if ((bool)A_FlagClothes.IsChecked)
            {
                CurrentObjectAppearance.Flags.Clothes = new AppearanceFlagClothes
                {
                    Slot = (uint)A_FlagClothesSlot.SelectedIndex
                };
            }
            else CurrentObjectAppearance.Flags.Clothes = null;

            if ((bool)A_FlagDefaultAction.IsChecked)
            {
                CurrentObjectAppearance.Flags.DefaultAction = new AppearanceFlagDefaultAction
                {
                    Action = (PLAYER_ACTION)A_FlagDefaultActionType.SelectedIndex
                };
            }
            else CurrentObjectAppearance.Flags.DefaultAction = null;

            if ((bool)A_FlagMarket.IsChecked)
            {
                CurrentObjectAppearance.Flags.Market = new AppearanceFlagMarket
                {
                    Category = (ITEM_CATEGORY)(A_FlagMarketCategory.SelectedIndex + 1),
                    TradeAsObjectId = (uint)A_FlagMarketTrade.Value,
                    ShowAsObjectId = (uint)A_FlagMarketShow.Value,
                    MinimumLevel = (uint)A_FlagMarketlevel.Value,
                };

                CurrentObjectAppearance.Flags.Market.Vocation = (VOCATION)A_FlagMarketProfession.SelectedIndex;
            }
            else
                CurrentObjectAppearance.Flags.Market = null;

            if ((bool)A_FlagWrap.IsChecked)
                CurrentObjectAppearance.Flags.Wrap = true;
            else if (CurrentObjectAppearance.Flags.HasWrap)
                CurrentObjectAppearance.Flags.ClearWrap();

            if ((bool)A_FlagUnwrap.IsChecked)
                CurrentObjectAppearance.Flags.Unwrap = true;
            else if (CurrentObjectAppearance.Flags.HasUnwrap)
                CurrentObjectAppearance.Flags.ClearUnwrap();

            if ((bool)A_FlagTopeffect.IsChecked)
                CurrentObjectAppearance.Flags.Topeffect = true;
            else if (CurrentObjectAppearance.Flags.HasTopeffect)
                CurrentObjectAppearance.Flags.ClearTopeffect();

            AppearanceSyncHelper.SyncMarketAndCyclopedia(CurrentObjectAppearance);

            if (ObjectMenu.SelectedIndex == 0)
            MainWindow.appearances.Outfit[ObjListView.SelectedIndex] = CurrentObjectAppearance.Clone();
            else if (ObjectMenu.SelectedIndex == 1)
                MainWindow.appearances.Object[ObjListView.SelectedIndex] = CurrentObjectAppearance.Clone();
            else if (ObjectMenu.SelectedIndex == 2)
                MainWindow.appearances.Effect[ObjListView.SelectedIndex] = CurrentObjectAppearance.Clone();
            else if (ObjectMenu.SelectedIndex == 3)
                MainWindow.appearances.Missile[ObjListView.SelectedIndex] = CurrentObjectAppearance.Clone();

            ShowList showList = ObjListView.SelectedItem as ShowList;
            AnimateSelectedListItem(showList);

            StatusBar.MessageQueue.Enqueue($"Saved Current Object.", null, null, null, false, true, TimeSpan.FromSeconds(2));
        }

        private void ObjectSave_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SaveCurrentObject();
        }

        private bool CopyFlagsFromCurrent(bool notify = true)
        {
            if (CurrentObjectAppearance?.Flags == null)
            {
                return false;
            }

            CurrentFlags = CurrentObjectAppearance.Flags.Clone();
            FlagClipboard.Copy(CurrentObjectAppearance.Flags);

            if (notify)
            {
                StatusBar.MessageQueue.Enqueue($"Copied Current Object Flags.", null, null, null, false, true, TimeSpan.FromSeconds(2));
            }

            return true;
        }

        private bool PasteFlagsOntoCurrent(bool notify = true)
        {
            if (CurrentObjectAppearance == null)
            {
                return false;
            }

            var clipboardFlags = FlagClipboard.GetClone() ?? CurrentFlags?.Clone();
            if (clipboardFlags == null)
            {
                if (notify)
                {
                    StatusBar.MessageQueue.Enqueue($"Copy Flags First.", null, null, null, false, true, TimeSpan.FromSeconds(2));
                }
                return false;
            }

            CurrentObjectAppearance.Flags = clipboardFlags;
            LoadCurrentObjectAppearances();

            if (notify)
            {
                StatusBar.MessageQueue.Enqueue($"Pasted Object Flags.", null, null, null, false, true, TimeSpan.FromSeconds(2));
            }

            return true;
        }

        private void ClearCurrentFlags(bool notify = true)
        {
            if (CurrentObjectAppearance == null)
            {
                return;
            }

            CurrentObjectAppearance.Flags = new AppearanceFlags();
            LoadCurrentObjectAppearances();

            if (notify)
            {
                StatusBar.MessageQueue.Enqueue($"Cleared all flags.", null, null, null, false, true, TimeSpan.FromSeconds(2));
            }
        }

        private void CopyObjectFlags(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CopyFlagsFromCurrent();
        }
        private void PasteObjectFlags(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            PasteFlagsOntoCurrent();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            base.OnClosed(e);
            Application.Current.Shutdown();
        }
        public class LowercaseContractResolver : DefaultContractResolver
        {
            protected override string ResolvePropertyName(string propertyName)
            {
                return propertyName.ToLower();
            }
        }

        private void Compile_Click(object sender, RoutedEventArgs e)
        {
            ComppileDialogHost.IsOpen = true;
            CompileBox.IsEnabled = true;
        }

        private async void CompileClient(object sender, RoutedEventArgs e)
        {
            bool isDatEditable = false;
            bool isSprEditable = false;
            string datfile = MainWindow._assetsPath + A_CompileName.Text + ".dat";
            string sprfile = MainWindow._assetsPath + A_CompileName.Text + ".spr";

            try
            {
                using (var fileStream = new FileStream(datfile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    isDatEditable = true;
                }
            }
            catch (IOException)
            {
                isDatEditable = false;
            }

            try
            {
                using (var fileStream = new FileStream(sprfile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    isSprEditable = true;
                }
            }
            catch (IOException)
            {
                isSprEditable = false;
            }

            if (isDatEditable && isSprEditable)
            {
                AppearanceSyncHelper.SyncMarketAndCyclopedia(MainWindow.appearances);
                LegacyAppearance.WriteLegacyDat(datfile, MainWindow.DatSignature, MainWindow.appearances, MainWindow.serverSetting.Version);
                var progress = new Progress<int>(percent =>
                {
                    LoadProgress.Value = percent;
                });
                CompileBox.IsEnabled = false;
                await Sprite.CompileSpritesAsync(sprfile, MainWindow.MainSprStorage, (bool)C_Transparent.IsChecked, MainWindow.SprSignature, progress);
            }
            else
                StatusBar.MessageQueue.Enqueue($".dat or .spr file is being used by another process or is not accessible.", null, null, null, false, true, TimeSpan.FromSeconds(2));

            ComppileDialogHost.IsOpen = false;
        }
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void SpriteExport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            List<ShowList> selectedItems = SprListView.SelectedItems.Cast<ShowList>().ToList();
            if (selectedItems.Any())
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "Bitmap Image (.bmp)|*.bmp|Gif Image (.gif)|*.gif|JPEG Image (.jpeg)|*.jpeg|Png Image (.png)|*.png",
                    FileName = " "
                };
                if (saveFileDialog.ShowDialog() == true)
                {
                    foreach (var item in selectedItems)
                    {
                        if (item.Image != null)
                        {
                            System.Drawing.Bitmap targetImg = new System.Drawing.Bitmap((int)item.Image.Width, (int)item.Image.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                            System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(targetImg);
                            if(saveFileDialog.FilterIndex != 4)
                                g.Clear(System.Drawing.Color.FromArgb(255, 255, 0, 255));
                            System.Drawing.Image image = System.Drawing.Image.FromStream(MainWindow.MainSprStorage.getSpriteStream(item.Id));
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.DrawImage(image, new System.Drawing.Rectangle(0, 0, targetImg.Width, targetImg.Height), new System.Drawing.Rectangle(0, 0, targetImg.Width, targetImg.Height), System.Drawing.GraphicsUnit.Pixel);
                            g.Dispose();
                            string directoryPath = System.IO.Path.GetDirectoryName(saveFileDialog.FileName);
                            switch (saveFileDialog.FilterIndex)
                            {
                                case 1:
                                    targetImg.Save(directoryPath + "\\" + item.Id.ToString() + ".bmp", System.Drawing.Imaging.ImageFormat.Bmp);
                                    break;
                                case 2:
                                    targetImg.Save(directoryPath + "\\" + item.Id.ToString() + ".gif", System.Drawing.Imaging.ImageFormat.Gif);
                                    break;
                                case 3:
                                    targetImg.Save(directoryPath + "\\" + item.Id.ToString() + ".jpg", System.Drawing.Imaging.ImageFormat.Jpeg);
                                    break;
                                case 4:
                                    targetImg.Save(directoryPath + "\\" + item.Id.ToString() + ".png", System.Drawing.Imaging.ImageFormat.Png);
                                    break;
                            }
                            targetImg.Dispose();
                        }
                    }
                }
            }
        }

        private void ImportSprite_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Bitmap Image (.bmp)|*.bmp|Gif Image (.gif)|*.gif|JPEG Image (.jpeg)|*.jpeg|Png Image (.png)|*.png";
            openFileDialog.Multiselect = true;
            int imported = 0;
            if (openFileDialog.ShowDialog() == true)
            {
                string[] selectedFiles = openFileDialog.FileNames;
                foreach (string filePath in selectedFiles)
                {
                    using (System.Drawing.Image originalImage = System.Drawing.Image.FromFile(filePath))
                    {
                        System.Drawing.Color magentaColor = System.Drawing.Color.Magenta;

                        int cropsX = originalImage.Width / 32;
                        int cropsY = originalImage.Height / 32;

                        for (int i = 0; i < cropsY; i++)
                        {
                            for (int j = 0; j < cropsX; j++)
                            {
                                System.Drawing.Rectangle cropRect = new System.Drawing.Rectangle(j * 32, i * 32, 32, 32);
                                using (System.Drawing.Bitmap croppedBitmap = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                                {
                                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(croppedBitmap))
                                    {
                                        g.Clear(System.Drawing.Color.Transparent);
                                        ImageAttributes imageAttributes = new ImageAttributes();
                                        imageAttributes.SetColorKey(magentaColor, magentaColor);

                                        g.DrawImage(
                                            originalImage,
                                            new System.Drawing.Rectangle(0, 0, 32, 32),
                                            cropRect.X, cropRect.Y, cropRect.Width, cropRect.Height,
                                            System.Drawing.GraphicsUnit.Pixel,
                                            imageAttributes
                                        );
                                    }

                                    MemoryStream imgMemory = new MemoryStream();
                                    croppedBitmap.Save(imgMemory, ImageFormat.Png);
                                    imgMemory.Position = 0;

                                    int sprId = MainWindow.SprLists.Count;
                                    MainWindow.SprLists[sprId] = imgMemory;
                                    MainWindow.AllSprList.Add(new ShowList() { Id = (uint)sprId });
                                    CollectionViewSource.GetDefaultView(SprListView.ItemsSource).Refresh();

                                    imported++;
                                }
                            }
                        }
                    }
                }

                if (imported > 0)
                {
                    StatusBar.MessageQueue.Enqueue($"Successfully imported {imported} image(s).", null, null, null, false, true, TimeSpan.FromSeconds(2));
                }
                else
                {
                    StatusBar.MessageQueue.Enqueue("No images imported.", null, null, null, false, true, TimeSpan.FromSeconds(2));
                }
            }
        }
        private void DeleteSprite_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var selectedSprites = SprListView.SelectedItems.Cast<ShowList>()
                .Where(item => item != null)
                .ToList();
            if (!selectedSprites.Any())
            {
                return;
            }

            CancelSpriteDrag();

            int previousIndex = SprListView.SelectedIndex;
            var spriteIds = selectedSprites.Select(item => (int)item.Id)
                .Distinct()
                .OrderByDescending(id => id)
                .ToList();

            int removedCount = 0;
            int resetCount = 0;
            int skippedCount = 0;
            int currentMax = GetHighestSpriteId();

            foreach (int spriteId in spriteIds)
            {
                if (spriteId < 0 || !MainWindow.SprLists.ContainsKey(spriteId))
                {
                    continue;
                }

                if (currentMax == -1)
                {
                    break;
                }

                if (spriteId != currentMax)
                {
                    if (OverwriteSpriteWithTransparent(spriteId))
                    {
                        resetCount++;
                    }
                    continue;
                }

                if (SpriteIdIsInUse((uint)spriteId))
                {
                    skippedCount++;
                    continue;
                }

                if (RemoveSpriteEntry(spriteId))
                {
                    removedCount++;
                    currentMax = GetHighestSpriteId();
                }
            }

            RefreshSpriteListView();
            SprListView.SelectedItems.Clear();

            if (SprListView.Items.Count > 0)
            {
                int fallbackIndex = Math.Max(0, Math.Min(previousIndex, SprListView.Items.Count - 1));
                SprListView.SelectedIndex = fallbackIndex;
            }
            else
            {
                SprListView.SelectedIndex = -1;
            }

            StatusBar.MessageQueue.Enqueue(
                BuildSpriteDeletionMessage(removedCount, resetCount, skippedCount),
                null,
                null,
                null,
                false,
                true,
                TimeSpan.FromSeconds(2));
        }
        private void ReplaceSprite_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Bitmap Image (.bmp)|*.bmp|Gif Image (.gif)|*.gif|JPEG Image (.jpeg)|*.jpeg|Png Image (.png)|*.png";
            if (openFileDialog.ShowDialog() == true)
            {
                using (System.Drawing.Image originalImage = System.Drawing.Image.FromFile(openFileDialog.FileName))
                {
                    System.Drawing.Color magentaColor = System.Drawing.Color.Magenta;
                    System.Drawing.Rectangle cropRect = new System.Drawing.Rectangle(0, 0, 32, 32);
                    using (System.Drawing.Bitmap croppedBitmap = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(croppedBitmap))
                        {
                            g.Clear(System.Drawing.Color.Transparent);
                            ImageAttributes imageAttributes = new ImageAttributes();
                            imageAttributes.SetColorKey(magentaColor, magentaColor);

                            g.DrawImage(
                                originalImage,
                                new System.Drawing.Rectangle(0, 0, 32, 32),
                                cropRect.X, cropRect.Y, cropRect.Width, cropRect.Height,
                                System.Drawing.GraphicsUnit.Pixel,
                                imageAttributes
                            );
                        }

                        MemoryStream imgMemory = new MemoryStream();
                        croppedBitmap.Save(imgMemory, ImageFormat.Png);
                        imgMemory.Position = 0;

                        int sprId = SprListView.SelectedIndex;
                        MainWindow.SprLists[sprId] = imgMemory;
                        CollectionViewSource.GetDefaultView(SprListView.ItemsSource).Refresh();
                    }
                }
                StatusBar.MessageQueue.Enqueue($"Sprite successfully replaced.", null, null, null, false, true, TimeSpan.FromSeconds(2));
            }
        }
        private void ExportObject_PreviewMouseLeftButtonDown(object sender, RoutedEventArgs e)
        {

            List<ShowList> selectedItems = ObjListView.SelectedItems.Cast<ShowList>().ToList();
            if (selectedItems.Any())
            {
                ObjListViewSelectedIndex.Value = (int)selectedItems.Last().Id;
                List<Appearance> appearances = new List<Appearance>();
                foreach (var item in selectedItems)
                {
                    if (ObjectMenu.SelectedIndex == 0)
                        appearances.Add(MainWindow.appearances.Outfit[(int)item.Id - 1]);
                    else if (ObjectMenu.SelectedIndex == 1)
                        appearances.Add(MainWindow.appearances.Object[(int)item.Id - 100]);
                    else if (ObjectMenu.SelectedIndex == 2)
                        appearances.Add(MainWindow.appearances.Effect[(int)item.Id - 1]);
                    else if (ObjectMenu.SelectedIndex == 3)
                        appearances.Add(MainWindow.appearances.Missile[(int)item.Id - 1]);
                }
                if(ObdDecoder.Export(appearances))
                    StatusBar.MessageQueue.Enqueue($"Successfully exported objects.", null, null, null, false, true, TimeSpan.FromSeconds(2));
            }
        }
        
        private void OTBEditor_Click(object sender, RoutedEventArgs e)
        {
            OTBEditor oTBEditor = new OTBEditor(this, true);
            oTBEditor.Show();
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            ImportManager importerManager = new ImportManager(this);
            importerManager.Show();
        }

        private void ObjectClone_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            List<ShowList> selectedItems = ObjListView.SelectedItems.Cast<ShowList>().ToList();
            if (selectedItems.Any())
            {
                ObjListViewSelectedIndex.Value = (int)selectedItems.Last().Id;

                uint? lastId = null;
                foreach (var item in selectedItems)
                {
                    lastId = DuplicateAppearance(item);
                }

                if (lastId.HasValue)
                {
                    UpdateShowList(ObjectMenu.SelectedIndex);
                    ObjListViewSelectedIndex.Value = (int)lastId.Value;
                    StatusBar.MessageQueue.Enqueue($"Successfully duplicated {selectedItems.Count} {(selectedItems.Count == 1 ? "object" : "objects")}.", null, null, null, false, true, TimeSpan.FromSeconds(2));
                }
            }
        }

        private void SearchItem_Click(object sender, RoutedEventArgs e)
        {
            SearchWindow searchWindow = new SearchWindow(this, true);
            searchWindow.Show();
        }

        private void DeleteObject_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ObjListView.SelectedItem is not ShowList selectedShowList)
            {
                return;
            }

            if (!TryGetActiveCollections(out var appearanceCollection, out var showListCollection))
            {
                return;
            }

            int selectedIndex = ObjListView.SelectedIndex;
            if (selectedIndex < 0)
            {
                return;
            }

            if (selectedIndex == ObjListView.Items.Count - 1)
            {
                var appearanceToRemove = appearanceCollection.FirstOrDefault(a => a.Id == selectedShowList.Id);
                if (appearanceToRemove != null)
                {
                    appearanceCollection.Remove(appearanceToRemove);
                }
                showListCollection.Remove(selectedShowList);
            }
            else
            {
                var selectedObject = appearanceCollection.FirstOrDefault(a => a.Id == selectedShowList.Id);
                if (selectedObject == null)
                {
                    return;
                }

                selectedObject.Flags = new AppearanceFlags();

                FrameGroup frameGroup = new FrameGroup
                {
                    SpriteInfo = new SpriteInfo
                    {
                        PatternWidth = 1,
                        PatternHeight = 1,
                        PatternSize = 32,
                        PatternLayers = 1,
                        PatternX = 1,
                        PatternY = 1,
                        PatternZ = 1,
                        PatternFrames = 1
                    },
                    FixedFrameGroup = FIXED_FRAME_GROUP.OutfitIdle
                };

                frameGroup.SpriteInfo.SpriteId.Add(0);
                selectedObject.FrameGroup.Clear();
                selectedObject.FrameGroup.Add(frameGroup);

                if (MainWindow.SprLists.TryGetValue(0, out var placeholderStream))
                {
                    selectedShowList.Image = Utils.BitmapToBitmapImage(placeholderStream);
                }
                else
                {
                    selectedShowList.Image = null;
                }
            }

            RefreshLegacyListView();
            if (ObjListView.Items.Count > 0)
            {
                ObjListView.SelectedIndex = Math.Min(selectedIndex, ObjListView.Items.Count - 1);
            }
            else
            {
                ObjListView.SelectedIndex = -1;
            }

            StatusBar.MessageQueue.Enqueue($"Object successfully deleted.", null, null, null, false, true, TimeSpan.FromSeconds(2));

        }

        private void NewObject_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Appearance newObject = new Appearance();

            newObject.Flags = new AppearanceFlags();

            FrameGroup frameGroup = new FrameGroup();
            frameGroup.SpriteInfo = new SpriteInfo();
            frameGroup.FixedFrameGroup = FIXED_FRAME_GROUP.OutfitIdle;

            frameGroup.SpriteInfo.PatternWidth = 1;
            frameGroup.SpriteInfo.PatternHeight = 1;
            frameGroup.SpriteInfo.PatternSize = 32;

            frameGroup.SpriteInfo.PatternLayers = 1;
            frameGroup.SpriteInfo.PatternX = 1;
            frameGroup.SpriteInfo.PatternY = 1;
            frameGroup.SpriteInfo.PatternZ = 1;
            frameGroup.SpriteInfo.PatternFrames = 1;

            frameGroup.SpriteInfo.SpriteId.Add(0);

            newObject.FrameGroup.Add(frameGroup);

            if (ObjectMenu.SelectedIndex == 0)
            {
                newObject.AppearanceType = APPEARANCE_TYPE.AppearanceOutfit;
                newObject.Id = (uint)(MainWindow.appearances.Outfit.Count + 1);
                MainWindow.appearances.Outfit.Add(newObject);
                ThingsOutfit.Add(new ShowList() { Id = newObject.Id });
            }
            else if (ObjectMenu.SelectedIndex == 1)
            {
                newObject.AppearanceType = APPEARANCE_TYPE.AppearanceObject;
                newObject.Id = (uint)(MainWindow.appearances.Object.Count + 100);
                MainWindow.appearances.Object.Add(newObject);
                ThingsItem.Add(new ShowList() { Id = newObject.Id });
            }
            else if (ObjectMenu.SelectedIndex == 2)
            {
                newObject.AppearanceType = APPEARANCE_TYPE.AppearanceEffect;
                newObject.Id = (uint)(MainWindow.appearances.Effect.Count + 1);
                MainWindow.appearances.Effect.Add(newObject);
                ThingsEffect.Add(new ShowList() { Id = newObject.Id });

            }
            else if (ObjectMenu.SelectedIndex == 3)
            {
                newObject.AppearanceType = APPEARANCE_TYPE.AppearanceMissile;
                newObject.Id = (uint)(MainWindow.appearances.Missile.Count + 1);
                MainWindow.appearances.Missile.Add(newObject);
                ThingsMissile.Add(new ShowList() { Id = newObject.Id });

            }

            ObjListView.SelectedItem = ObjListView.Items[^1];
            StatusBar.MessageQueue.Enqueue($"Object successfully created.", null, null, null, false, true, TimeSpan.FromSeconds(2));
        }

        private void SprSynchronized_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo.Animation != null)
                CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo.Animation.AnimationMode = (bool)SprSynchronized.IsChecked ? ANIMATION_ANIMATION_MODE.AnimationSynchronized : ANIMATION_ANIMATION_MODE.AnimationAsynchronized;
        }
        private void SprDefaultPhase_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo.Animation != null)
                CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo.Animation.DefaultStartPhase = (uint)SprDefaultPhase.Value;
        }
        private void SprLoopCount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo.Animation != null)
                CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo.Animation.LoopCount = (uint)SprLoopCount.Value;
        }
        private void SprPhaseMin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo.Animation != null)
                CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo.Animation.SpritePhase[(int)(SprFramesSlider.Value / SprFramesSlider.TickFrequency)].DurationMin = (uint)SprPhaseMin.Value;
        }

        private void SprPhaseMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo.Animation != null)
                CurrentObjectAppearance.FrameGroup[(int)SprGroupSlider.Value].SpriteInfo.Animation.SpritePhase[(int)(SprFramesSlider.Value / SprFramesSlider.TickFrequency)].DurationMax = (uint)SprPhaseMax.Value;
        }

        public void AnimateSelectedListItem(ShowList showList)
        {
            // Find the ListViewItem for the selected item
            var listViewItem = ObjListView.ItemContainerGenerator.ContainerFromItem(showList) as ListViewItem;
            if (listViewItem != null)
            {
                // Find the Image control within the ListViewItem
                var imageControl = Utils.FindVisualChild<Image>(listViewItem);
                if (imageControl != null)
                {
                    showList.Images.Clear();

                    Appearance appearance = null;

                    if (ObjectMenu.SelectedIndex == 0)
                        appearance = MainWindow.appearances.Outfit.FirstOrDefault(o => o.Id == showList.Id);
                    else if (ObjectMenu.SelectedIndex == 1)
                        appearance = MainWindow.appearances.Object.FirstOrDefault(o => o.Id == showList.Id);
                    else if (ObjectMenu.SelectedIndex == 2)
                        appearance = MainWindow.appearances.Effect.FirstOrDefault(o => o.Id == showList.Id);
                    else if (ObjectMenu.SelectedIndex == 3)
                        appearance = MainWindow.appearances.Missile.FirstOrDefault(o => o.Id == showList.Id);

                    for (int i = 0; i < appearance.FrameGroup[0].SpriteInfo.PatternFrames; i++)
                    {
                        BitmapImage imageFrame = Utils.BitmapToBitmapImage(LegacyAppearance.GetObjectImage(appearance, MainWindow.MainSprStorage, i));
                        showList.Images.Add(imageFrame);
                    }

                    showList.StartAnimation();
                }
            }
        }


        private void RefreshLegacyListView()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CollectionViewSource.GetDefaultView(ObjListView.ItemsSource)?.Refresh();
            }), DispatcherPriority.Background);
        }

        private void ObjListView_Drop(object sender, DragEventArgs e)
        {
            var targetContainer = Utils.FindAncestorOrSelf<ListViewItem>(e.OriginalSource as DependencyObject);
            if (targetContainer != null)
            {
                targetContainer.Opacity = 1;
            }

            if (e.Data.GetDataPresent(typeof(ShowList)))
            {
                var data = e.Data.GetData(typeof(ShowList)) as ShowList;
                if (data == null)
                {
                    return;
                }

                if (targetContainer?.DataContext is ShowList showList)
                {
                    if (ObjectMenu.SelectedIndex == 0)
                        ReplaceObjectAppearance = MainWindow.appearances.Outfit.FirstOrDefault(o => o.Id == showList.Id);
                    else if (ObjectMenu.SelectedIndex == 1)
                        ReplaceObjectAppearance = MainWindow.appearances.Object.FirstOrDefault(o => o.Id == showList.Id);
                    else if (ObjectMenu.SelectedIndex == 2)
                        ReplaceObjectAppearance = MainWindow.appearances.Effect.FirstOrDefault(o => o.Id == showList.Id);
                    else if (ObjectMenu.SelectedIndex == 3)
                        ReplaceObjectAppearance = MainWindow.appearances.Missile.FirstOrDefault(o => o.Id == showList.Id);
                }
            }
        }

        private void ObjListView_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(ShowList)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            ListViewItem listViewItem = Utils.FindAncestorOrSelf<ListViewItem>(e.OriginalSource as DependencyObject);
            if (listViewItem != null)
            {
                listViewItem.Opacity = 0.5;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void ObjListView_DragLeave(object sender, DragEventArgs e)
        {
            ListViewItem listViewItem = Utils.FindAncestorOrSelf<ListViewItem>(e.OriginalSource as DependencyObject);
            if (listViewItem != null)
            {
                listViewItem.Opacity = 1;
            }
        }
        private void About_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow aboutWindow = new AboutWindow();
            aboutWindow.Show();
        }

        private void ShowLogger(object sender, RoutedEventArgs e)
        {
            MainWindow.logView.Show();
        }

    }
    public class ArithmeticConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
            {
                return null;
            }

            double doubleValue = System.Convert.ToDouble(value);
            double doubleParameter = System.Convert.ToDouble(parameter);

            // Perform arithmetic operations here based on the parameter
            if (doubleParameter > 0)
            {
                // Addition
                double result = doubleValue + doubleParameter;
                return result;
            }
            else if (doubleParameter < 0)
            {
                // Subtraction
                double result = doubleValue - Math.Abs(doubleParameter);
                return result;
            }
            else
            {
                // Multiplication
                double result = doubleValue * doubleParameter;
                return result;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
            {
                return null;
            }

            double doubleValue = System.Convert.ToDouble(value);
            double doubleParameter = System.Convert.ToDouble(parameter);

            // Perform inverse arithmetic operations here based on the parameter
            if (doubleParameter > 0)
            {
                // Subtraction (inverse of addition)
                double result = doubleValue - doubleParameter;
                return result;
            }
            else if (doubleParameter < 0)
            {
                // Addition (inverse of subtraction)
                double result = doubleValue + Math.Abs(doubleParameter);
                return result;
            }
            else
            {
                // Division (inverse of multiplication)
                if (doubleValue == 0)
                {
                    return 0;
                }
                else
                {
                    double result = doubleValue / doubleParameter;
                    return result;
                }
            }
        }
    }
    public class NullableColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return Brushes.Transparent;
            }

            return new SolidColorBrush((Color)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BooleanInverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool booleanValue)
            {
                return !booleanValue;
            }

            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("BooleanInverter can only be used for one-way binding");
        }
    }

}
