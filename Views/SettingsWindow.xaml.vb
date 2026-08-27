Imports System.Windows

Namespace CreoPublisherApp

    Class SettingsWindow


        Private Sub SettingsWindow_Loaded(sender As Object, e As RoutedEventArgs)
            Dim vm = CType(DataContext, SettingsWindowViewModel)

            AddHandler vm.RequestClose, Sub()
                                            Me.Close()
                                        End Sub

            AddHandler vm.RequestShutdown, Sub()
                                               ' Close Settings window
                                               Me.Close()

                                               ' Close MainWindow explicitly
                                               If Application.Current.MainWindow IsNot Nothing Then
                                                   Application.Current.MainWindow.Close()
                                               End If

                                               ' Final safety shutdown
                                               Application.Current.Shutdown()
                                           End Sub
        End Sub


    End Class


End Namespace
