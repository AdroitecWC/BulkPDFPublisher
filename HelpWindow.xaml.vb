Imports System.Diagnostics
Imports System.Windows.Navigation
Namespace BulkPdfPublisher

    Partial Public Class HelpWindow
        Inherits Window

        Public Sub New()
            InitializeComponent()
        End Sub

        Private Sub Close_Click(sender As Object, e As RoutedEventArgs)
            Me.Close()
        End Sub

        Private Sub Hyperlink_RequestNavigate(sender As Object, e As RequestNavigateEventArgs)
            Process.Start(New ProcessStartInfo(e.Uri.AbsoluteUri) With {.UseShellExecute = True})
            e.Handled = True
        End Sub

    End Class

End Namespace
