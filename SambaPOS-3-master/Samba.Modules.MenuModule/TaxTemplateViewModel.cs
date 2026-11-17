using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using FluentValidation;
using Samba.Domain.Models.Accounts;
using Samba.Domain.Models.Menus;
using Samba.Localization.Properties;
using Samba.Presentation.Common.ModelBase;

namespace Samba.Modules.MenuModule
{
    [Export, PartCreationPolicy(CreationPolicy.NonShared)]
    public class TaxTemplateViewModel : EntityViewModelBaseWithMap<TaxTemplate, TaxTemplateMap, TaxTemplateMapViewModel>
    {
        private IEnumerable<AccountTransactionType> _accountTransactionTypes;
        public IEnumerable<AccountTransactionType> AccountTransactionTypes
        {
            get { return _accountTransactionTypes ?? (_accountTransactionTypes = Workspace.All<AccountTransactionType>()); }
        }

        public AccountTransactionType AccountTransactionType { get { return Model.AccountTransactionType; } set { Model.AccountTransactionType = value; } }

        private string[] _taxTypes;
        public string[] TaxTypes
        {
            get
            {
                return _taxTypes ?? (_taxTypes = new[] { "Yüzde (%)", "Tutar (TL)" });
            }
        }

        public string SelectedTaxType
        {
            get { return TaxTypes[Model.TaxType >= 0 && Model.TaxType < TaxTypes.Length ? Model.TaxType : 0]; }
            set { Model.TaxType = Array.IndexOf(TaxTypes, value); }
        }

        public int TaxType { get { return Model.TaxType; } set { Model.TaxType = value; } }
        public decimal Rate { get { return Model.Rate; } set { Model.Rate = value; } }
        public int Rounding { get { return Model.Rounding; } set { Model.Rounding = value; } }

        public override Type GetViewType()
        {
            return typeof(TaxTemplateView);
        }

        public override string GetModelTypeString()
        {
            return Resources.TaxTemplate;
        }

        protected override AbstractValidator<TaxTemplate> GetValidator()
        {
            return new TaxTemplateValidator();
        }

        protected override void Initialize()
        {
            base.Initialize();
            MapController = new MapController<TaxTemplateMap, TaxTemplateMapViewModel>(Model.TaxTemplateMaps, Workspace);
        }
    }

    internal class TaxTemplateValidator : EntityValidator<TaxTemplate>
    {
        public TaxTemplateValidator()
        {
            RuleFor(x => x.AccountTransactionType).NotNull();
        }
    }
}
