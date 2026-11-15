using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Ignis.Frontend.Views.Shared
{
    /// <summary>
    /// Interaction logic for PrimaryButton.xaml
    /// </summary>
    public partial class PrimaryButton : UserControl
    {
        public PrimaryButton()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(PrimaryButton));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public static readonly DependencyProperty CommanProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(PrimaryButton));

        public ICommand Command
        {
            get => (ICommand)GetValue(CommanProperty);
            set => SetValue(CommanProperty, value);
        }

        public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(PrimaryButton));

        public object CommandParameter
        {
            get => GetValue(CommandParameterProperty);
            set => SetValue(CommandParameterProperty, value);
        }
    }
}
