using System.Collections.ObjectModel;
using System.Windows;

namespace Assets_Editor
{
    public partial class AssetPreviewDialog : Window
    {
        public ObservableCollection<PreviewEntry> Entries { get; } = new ObservableCollection<PreviewEntry>();
        public string PreviewMessage { get; }
        public string PreviewPath { get; }

        internal AssetPreviewDialog(AssetReloadService.AssetPreviewInfo previewInfo)
        {
            InitializeComponent();
            DataContext = this;

            if (previewInfo == null)
            {
                PreviewMessage = "No client selected.";
                PreviewPath = string.Empty;
                return;
            }

            string typeLabel = previewInfo.Kind == AssetReloadService.AssetFolderKind.Modern ? "modern" : "legacy";
            PreviewMessage = $"Confirm opening the {typeLabel} client.";
            PreviewPath = previewInfo.AssetsPath;

            AddEntry("Type", previewInfo.Kind == AssetReloadService.AssetFolderKind.Modern ? "Modern" : "Legacy");
            AddEntry("Path", previewInfo.AssetsPath);
            if (!string.IsNullOrWhiteSpace(previewInfo.DatPath))
            {
                AddEntry("DAT File", previewInfo.DatPath);
            }
            if (!string.IsNullOrWhiteSpace(previewInfo.SprPath))
            {
                AddEntry("SPR File", previewInfo.SprPath);
            }

            AddEntry("Objects", previewInfo.ObjectCount.ToString());
            AddEntry("Outfits", previewInfo.OutfitCount.ToString());
            AddEntry("Effects", previewInfo.EffectCount.ToString());
            AddEntry("Missiles", previewInfo.MissileCount.ToString());

            if (previewInfo.Kind == AssetReloadService.AssetFolderKind.Modern)
            {
                AddEntry("Catalogs", previewInfo.CatalogCount.ToString());
                AddEntry("Sprite Sheets", previewInfo.SpriteCatalogCount.ToString());
            }
            else
            {
                AddEntry("Legacy Version", previewInfo.LegacyVersion.ToString());
                AddEntry("DAT Signature", $"0x{previewInfo.DatSignature:X8}");
                AddEntry("SPR Signature", $"0x{previewInfo.SprSignature:X8}");
                AddEntry("Transparent Sprites", previewInfo.TransparentSprites ? "Yes" : "No");
            }
        }

        private void AddEntry(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            Entries.Add(new PreviewEntry
            {
                Label = label,
                Value = value ?? string.Empty
            });
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        public class PreviewEntry
        {
            public string Label { get; set; }
            public string Value { get; set; }
        }
    }
}
