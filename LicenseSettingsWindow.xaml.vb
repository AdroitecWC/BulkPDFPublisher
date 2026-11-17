Imports System.Diagnostics
Imports System.Windows

Namespace CreoPublisherApp

    Class LicenseSettingsWindow

        Private Sub LicenseSettingsWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
            Dim vm = TryCast(DataContext, LicenseSettingsViewModel)

            If vm IsNot Nothing Then
                AddHandler vm.RequestClose,
                    Sub()
                        Me.Close()
                    End Sub

                AddHandler vm.RequestShutdown,
                    Sub()
                        Me.Close()

                        ' Deactivate/Change License leave the install with no valid license --
                        ' relaunch the exe so the activation window comes back up on its own
                        ' instead of leaving the user to find and reopen it manually.
                        Try
                            Dim exePath = Process.GetCurrentProcess().MainModule?.FileName
                            If Not String.IsNullOrEmpty(exePath) Then Process.Start(exePath)
                        Catch
                            ' Best-effort -- if relaunch fails, the app still closes below.
                        End Try

                        If Application.Current.MainWindow IsNot Nothing Then
                            Application.Current.MainWindow.Close()
                        End If

                        Application.Current.Shutdown()
                    End Sub
            End If

            RefreshBorrowPanels()
        End Sub

        ''' <summary>Shows the Borrow panel, the "Borrow active until X" panel, or the
        ''' "no floating license" note, depending on whether a borrow is currently active
        ''' and whether a floating license is configured at all. Re-run after any
        ''' borrow/return so the UI stays in sync.</summary>
        Private Sub RefreshBorrowPanels()
            Dim expiresLocal As DateTime
            If LicenseManager.TryGetActiveBorrow(expiresLocal) Then
                pnlBorrow.Visibility = Visibility.Collapsed
                pnlBorrowActive.Visibility = Visibility.Visible
                txtBorrowUnavailable.Visibility = Visibility.Collapsed
                txtBorrowStatus.Text = $"Borrow active until {expiresLocal:yyyy-MM-dd HH:mm}"
            ElseIf LicenseManager.IsFloatingConfigured() Then
                pnlBorrow.Visibility = Visibility.Visible
                pnlBorrowActive.Visibility = Visibility.Collapsed
                txtBorrowUnavailable.Visibility = Visibility.Collapsed
            Else
                pnlBorrow.Visibility = Visibility.Collapsed
                pnlBorrowActive.Visibility = Visibility.Collapsed
                txtBorrowUnavailable.Visibility = Visibility.Visible
            End If
        End Sub

        Private Sub btnBorrow_Click(sender As Object, e As RoutedEventArgs)
            Dim amount As Double
            If Not Double.TryParse(txtBorrowAmount.Text.Trim(), amount) OrElse amount <= 0 Then
                MessageBox.Show("Enter a positive number to borrow.", "Borrow",
                    MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If

            Dim isHours = cmbBorrowUnit.SelectedIndex = 1
            Dim hours = If(isHours, amount, amount * 24)

            btnBorrow.IsEnabled = False
            Try
                Dim message As String = ""
                Dim ok = LicenseManager.BorrowSeat(hours, message)
                MessageBox.Show(message, If(ok, "Borrowed", "Borrow Failed"),
                    MessageBoxButton.OK, If(ok, MessageBoxImage.Information, MessageBoxImage.Error))
                If ok Then RefreshBorrowPanels()
            Finally
                btnBorrow.IsEnabled = True
            End Try
        End Sub

        Private Sub btnReturnBorrow_Click(sender As Object, e As RoutedEventArgs)
            btnReturnBorrow.IsEnabled = False
            Try
                Dim message As String = ""
                Dim ok = LicenseManager.ReturnBorrowedSeat(message)
                MessageBox.Show(message, If(ok, "Returned", "Return Failed"),
                    MessageBoxButton.OK, If(ok, MessageBoxImage.Information, MessageBoxImage.Warning))
                RefreshBorrowPanels()
            Finally
                btnReturnBorrow.IsEnabled = True
            End Try
        End Sub

    End Class

End Namespace
