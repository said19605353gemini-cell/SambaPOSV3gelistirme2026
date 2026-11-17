using System.ComponentModel.Composition;
using Microsoft.Practices.Prism.MefExtensions.Modularity;
using Microsoft.Practices.Prism.Regions;
using Samba.Presentation.Common;
using Samba.Presentation.Services;
using Samba.Presentation.Services.Common;

namespace Samba.Modules.PosModule
{
    [ModuleExport(typeof(KitchenModule))]
    public class KitchenModule : VisibleModuleBase
    {
        private readonly IRegionManager _regionManager;
        private readonly KitchenView _kitchenView;
        private readonly KitchenViewModel _kitchenViewModel;
        private readonly IApplicationState _applicationState;

        [ImportingConstructor]
        public KitchenModule(IRegionManager regionManager, KitchenView kitchenView, KitchenViewModel kitchenViewModel, IApplicationState applicationState)
            : base(regionManager, AppScreens.KitchenView)
        {
            _regionManager = regionManager;
            _kitchenView = kitchenView;
            _kitchenViewModel = kitchenViewModel;
            _applicationState = applicationState;

            SetNavigationCommand("Mutfak Ekranı", "Ortak", "Images/Run.png", 15);
        }

        protected override bool CanNavigate(string arg)
        {
            return _applicationState.IsCurrentWorkPeriodOpen;
        }

        protected override void OnNavigate(string obj)
        {
            base.OnNavigate(obj);
            _kitchenViewModel.LoadKitchenOrders(false);
        }

        public override object GetVisibleView()
        {
            return _kitchenView;
        }

        protected override void OnInitialization()
        {
            _regionManager.RegisterViewWithRegion(RegionNames.MainRegion, typeof(KitchenView));
        }
    }
}
