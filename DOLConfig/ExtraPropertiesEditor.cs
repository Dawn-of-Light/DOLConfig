using System;
using System.Windows.Forms;

namespace DOLConfig
{
    public partial class ExtraPropertiesEditor : Form
    {
        public string PropertyName { get; set; } = null;
        public string PropertyType { get; set; } = null;
        public object PropertyValue { get; set; } = null;
        public string PropertyDescription { get; set; } = null;

        public ExtraPropertiesEditor(string property_name, string property_type, object property_value, string property_description)
        {
            InitializeComponent();

            this.PropertyName = property_name;
            this.property_name_textbox.Text = property_name;

            if (this.property_type_selectbox.Items.Contains(property_type))
            {
                this.PropertyType = property_type;
                this.property_type_selectbox.SelectedItem = property_type;
            }
            else
            {
                if(property_type.Length > 0) this.edit_property_error_label.Text = "Unknown type: " + property_type;
            }

            this.PropertyValue = property_value;
            this.property_value_textbox.Text = Convert.ToString(property_value);

            if (property_description.Length > 0)
            {
                this.PropertyDescription = property_description;
                this.property_description_label.Text = Convert.ToString(property_description);
            }
        }

        private void save_button_Click(object sender, EventArgs e)
        {
            try
            {
                switch (this.PropertyType)
                {
                    case "string":
                        Convert.ToString(PropertyValue);
                        break;
                    case "integer":
                        Convert.ToInt32(PropertyValue);
                        break;
                    case "boolean":
                        Convert.ToBoolean(PropertyValue);
                        break;
                }
            }
            catch (FormatException)
            {
                this.edit_property_error_label.Text = "The value must be a type of " + this.PropertyType;
                return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void cancel_button_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void property_name_textbox_TextChanged(object sender, EventArgs e)
        {
            this.PropertyName = ((TextBox)sender).Text.Trim();
        }

        private void property_type_selectbox_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.PropertyType = ((ComboBox)sender).SelectedItem.ToString().Trim();
        }

        private void property_value_textbox_TextChanged(object sender, EventArgs e)
        {
            this.PropertyValue = ((TextBox)sender).Text.Trim();
        }
    }
}
