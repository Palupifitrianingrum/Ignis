using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Ignis.Frontend.Views.Auth;

namespace Ignis.Frontend.Navigation
{
    public class DialogService : IDialogService
    {
        private readonly IServiceProvider _serviceProvider;

        public DialogService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public bool? ShowLoginDialog()
        {
            var loginWindow = _serviceProvider.GetRequiredService<LoginView>();
            return loginWindow.ShowDialog();
        }
    }
}
