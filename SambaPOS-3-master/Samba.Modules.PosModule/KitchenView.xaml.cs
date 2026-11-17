using System.ComponentModel.Composition;
using System.Windows.Controls;

namespace Samba.Modules.PosModule
{
    [Export]
    public partial class KitchenView : UserControl
    {
        [ImportingConstructor]
        public KitchenView(KitchenViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
