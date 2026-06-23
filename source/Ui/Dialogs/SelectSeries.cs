using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Resonalyze
{
    public partial class SelectSeries : Form
    {
        public SelectSeries()
        {
            InitializeComponent();
            StartPosition = FormStartPosition.CenterParent;
        }

        public void AddOption(string option)
        {
            comboBox1.Items.Add(option);
        }

        public void SetSelection(int sel)
        {
            comboBox1.SelectedIndex = sel;
        }

        public int GetSelect()
        {
            return comboBox1.SelectedIndex;
        }
    }
}
