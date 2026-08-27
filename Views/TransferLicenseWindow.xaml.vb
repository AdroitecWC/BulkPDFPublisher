Imports System.Windows

Namespace CreoPublisherApp

    Public Class TransferLicenseWindow

        Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
            Dim vm = TryCast(DataContext, TransferLicenseViewModel)
            If vm IsNot Nothing Then
                AddHandler vm.RequestClose,
                    Sub(result As Boolean)
                        Me.DialogResult = result
                        Me.Close()
                    End Sub
            End If
        End Sub

    End Class

End Namespace
