using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using RobloxScriptExplorer.Logica;

namespace RobloxScriptExplorer.Interfaz
{
    public partial class AddScriptModal : Window
    {
        public string ScriptName { get; private set; } = "NewScript";
        public string ScriptType { get; private set; } = "Script";
        public int ParentId { get; private set; } = 0;

        public AddScriptModal(IEnumerable<RobloxInstance> candidateParents)
        {
            InitializeComponent();

            foreach (var inst in candidateParents)
            {
                var item = new ComboBoxItem
                {
                    Content = $"{inst.DisplayIcon} {inst.Name} (ID {inst.Id})",
                    Tag = inst.Id
                };
                CmbParent.Items.Add(item);
            }

            if (CmbParent.Items.Count > 0)
            {
                CmbParent.SelectedIndex = 0;
            }
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Por favor escribe un nombre válido para el script.", "Nombre requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ScriptName = name;
            if (CmbType.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string tagStr)
            {
                ScriptType = tagStr;
            }

            if (CmbParent.SelectedItem is ComboBoxItem parentItem && parentItem.Tag is int pid)
            {
                ParentId = pid;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
