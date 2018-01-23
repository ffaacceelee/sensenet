﻿using SenseNet.ContentRepository;
using SenseNet.Portal.Virtualization;
using System.Web;
using SenseNet.Services.Virtualization;

namespace SenseNet.ApplicationModel
{
    public class LogoutAction : UrlAction
    {
        public override string Uri => "/" + Configuration.Services.ODataAndRoot + "/Logout";

        public override bool IsODataOperation => true;
        public override bool CausesStateChange => false;
        public override bool IsHtmlOperation => false;
        public override ActionParameter[] ActionParameters { get; } = { new ActionParameter("ultimateLogout", typeof(bool), false) };

        private IUltimateLogoutProvider _logoutProvider;
        public override void Initialize(Content context, string backUri, Application application, object parameters)
        {
            base.Initialize(context, backUri, application, parameters);
            _logoutProvider = new UltimateLogoutProvider();
            if (PortalContext.Current.AuthenticationMode == "Windows" || !User.Current.IsAuthenticated)
            {
                this.Visible = false;
            }
        }

        public override object Execute(Content content, params object[] parameters)
        {
            var ultimateLogout = parameters != null && parameters.Length > 0 && parameters[0] != null && (bool)parameters[0];
            _logoutProvider.Logout(ultimateLogout);

            var backUrl = PortalContext.Current.BackUrl;
            var back = string.IsNullOrWhiteSpace(backUrl) ? "/" : backUrl;

            HttpContext.Current.Response.Redirect(back, true);

            return null;
        }
    }
}
