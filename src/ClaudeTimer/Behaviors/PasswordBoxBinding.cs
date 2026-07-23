using System.Windows;
using System.Windows.Controls;

namespace ClaudeTimer.Behaviors;

public static class PasswordBoxBinding
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxBinding),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged));

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(PasswordBoxBinding),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(PasswordBoxBinding));

    public static string GetBoundPassword(DependencyObject value) =>
        (string)value.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject value, string password) =>
        value.SetValue(BoundPasswordProperty, password);

    public static bool GetIsEnabled(DependencyObject value) =>
        (bool)value.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject value, bool enabled) =>
        value.SetValue(IsEnabledProperty, enabled);

    private static void OnBoundPasswordChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not PasswordBox passwordBox ||
            (bool)passwordBox.GetValue(IsUpdatingProperty))
        {
            return;
        }

        passwordBox.Password = e.NewValue as string ?? string.Empty;
    }

    private static void OnIsEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not PasswordBox passwordBox)
        {
            return;
        }

        if ((bool)e.OldValue)
        {
            passwordBox.PasswordChanged -= OnPasswordChanged;
        }

        if ((bool)e.NewValue)
        {
            passwordBox.PasswordChanged += OnPasswordChanged;
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var passwordBox = (PasswordBox)sender;
        passwordBox.SetValue(IsUpdatingProperty, true);
        SetBoundPassword(passwordBox, passwordBox.Password);
        passwordBox.SetValue(IsUpdatingProperty, false);
    }
}
