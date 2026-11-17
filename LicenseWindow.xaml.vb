Imports System.IO
Imports System.Windows
Imports Microsoft.Win32

Namespace CreoPublisherApp
    Partial Public Class LicenseWindow
        Inherits Window

        Private ReadOnly requestCode As String = LicenseManager.GenerateLicenseCode()

        Public Sub New()
            InitializeComponent()
            txtSystemName.Text = Environment.MachineName
            txtRequestCode.Text = requestCode
            txtToolName.Text = LicenseManager.AppProduct
            Title = $"{LicenseManager.AppProduct} — License Activation"

            ' Pre-fill the last server address used (if any) -- Change License clears the
            ' saved override, and this field otherwise starts blank every time, forcing a
            ' blind retype where a typo/blank entry looks exactly like the server being down.
            txtNetworkServer.Text = LicenseManager.GetLastServerAddress()

            ' Show expiry date if already licensed
            If LicenseManager.CheckLicense() Then
                Dim licPath = LicenseManager.GetLicensePath()
                If File.Exists(licPath) Then
                    Try
                        Using reader As New StreamReader(licPath)
                            Dim line = reader.ReadLine()
                            If line IsNot Nothing AndAlso line.StartsWith("LastDate:") Then
                                Dim expDate = line.Replace("LastDate:", "").Trim()
                                lblExpiry.Text = $"License valid until {expDate}"
                                lblExpiry.Visibility = Visibility.Visible
                            End If
                        End Using
                    Catch
                        ' Ignore read errors
                    End Try
                End If
            End If
        End Sub

        Private Sub btnBrowse_Click(sender As Object, e As RoutedEventArgs)
            Dim ofd As New OpenFileDialog With {
                .Filter = "License Files (*.lic)|*.lic"
            }
            If ofd.ShowDialog() = True Then
                txtFilePath.Text = ofd.FileName
            End If
        End Sub

        ''' <summary>
        ''' Shown whenever a floating checkout can't reach the NLM -- during
        ''' activation itself, not just on a later launch of an already-configured
        ''' install. Yes leaves the window open so the user can enter a different
        ''' license/server; No closes without activating.
        ''' </summary>
        Private Sub ShowServerUnavailablePrompt()
            Dim attemptedUrl = LicenseManager.LastServerUrl
            Dim urlLine = If(attemptedUrl = "", "", vbCrLf & $"Address tried: {attemptedUrl}" & vbCrLf)

            Dim answer = MessageBox.Show(
                "Network License Manager is not available." & vbCrLf &
                "The Network License Manager could not be reached — it may be offline, or the server address/port may be wrong." & vbCrLf &
                urlLine & vbCrLf &
                "Click Yes to change the license, or No to close.",
                "Network License Manager Not Available", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            If answer = MessageBoxResult.No Then
                DialogResult = False
                Close()
            End If
        End Sub

        Private Sub btnActivate_Click(sender As Object, e As RoutedEventArgs)
            Dim licenseKey = txtLicenseKey.Text.Trim()
            If licenseKey <> "" Then
                Dim keyMessage As String = ""
                Dim ok = LicenseManager.ActivateWithKey(licenseKey, txtNetworkServer.Text.Trim(), keyMessage)
                If ok Then
                    MessageBox.Show(keyMessage, "Success", MessageBoxButton.OK, MessageBoxImage.Information)
                    DialogResult = True
                    Close()
                ElseIf LicenseManager.LastActivationError = LicenseManager.LicenseError.ServerUnreachable Then
                    ShowServerUnavailablePrompt()
                Else
                    MessageBox.Show(keyMessage, "Activation Failed", MessageBoxButton.OK, MessageBoxImage.Error)
                End If
                Return
            End If

            Dim selectedFile = txtFilePath.Text.Trim()
            If Not File.Exists(selectedFile) Then
                MessageBox.Show("License file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                Return
            End If

            Try
                Dim targetPath = LicenseManager.GetLicensePath()
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath))
                File.Copy(selectedFile, targetPath, True)

                ' Activate (validates the machine code match and flips Activated:True —
                ' node-locked files arrive from the web portal with Activated:False).
                ' Floating licenses are already pre-activated by the server.
                LicenseManager.ActivateLicense()

                Dim result = LicenseManager.CheckLicenseDetailed()

                Select Case result
                    Case LicenseManager.LicenseError.None
                        MessageBox.Show("License Activated!", "Success", MessageBoxButton.OK, MessageBoxImage.Information)
                        DialogResult = True
                        Close()

                    Case LicenseManager.LicenseError.ServerUnreachable
                        ShowServerUnavailablePrompt()

                    Case LicenseManager.LicenseError.NoSeatsAvailable
                        MessageBox.Show(
                            $"No seats available ({LicenseManager.LastSeatsInUse}/{LicenseManager.LastSeatsMax} in use)." & vbCrLf &
                            "Ask another user to close the application to free a seat, or contact Adroitec to increase your seat count.",
                            "No Seats Available", MessageBoxButton.OK, MessageBoxImage.Exclamation)

                    Case LicenseManager.LicenseError.InvalidLicense
                        MessageBox.Show(
                            "The server does not recognise this license." & vbCrLf &
                            "Please contact your administrator.",
                            "Invalid License", MessageBoxButton.OK, MessageBoxImage.Error)

                    Case LicenseManager.LicenseError.WrongProduct
                        MessageBox.Show(
                            "This license was issued for a different Adroitec product." & vbCrLf &
                            "Please use the license generated for Bulk PDF publish.",
                            "Wrong Product", MessageBoxButton.OK, MessageBoxImage.Error)

                    Case LicenseManager.LicenseError.Expired
                        MessageBox.Show("This license has expired. Please renew it.", "Expired",
                                MessageBoxButton.OK, MessageBoxImage.Error)

                    Case LicenseManager.LicenseError.LicenseTransferred
                        MessageBox.Show(
                            "This license was already transferred to another machine." & vbCrLf &
                            "Please contact your administrator for a new license.",
                            "License Transferred", MessageBoxButton.OK, MessageBoxImage.Error)

                    Case LicenseManager.LicenseError.MachineMismatch
                        MessageBox.Show(
                            "This license was not issued for this machine." & vbCrLf & vbCrLf &
                            "Your Request Code:" & vbCrLf & $"  {requestCode}" & vbCrLf & vbCrLf &
                            "Contact Adroitec with this Request Code to get a new license.",
                            "Machine Mismatch", MessageBoxButton.OK, MessageBoxImage.Error)

                    Case Else
                        MessageBox.Show($"License Not Activated ({result}).", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error)
                End Select

            Catch ex As Exception
                MessageBox.Show("Activation failed: " & ex.Message, "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End Sub

        Private Sub btnConnectServer_Click(sender As Object, e As RoutedEventArgs)
            Dim address = txtNetworkServer.Text.Trim()
            If address = "" Then
                MessageBox.Show("Enter a server address, e.g. vizserver:1122.", "Host Name",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation)
                Return
            End If

            btnConnectServer.IsEnabled = False
            Try
                Dim message As String = ""
                Dim ok = LicenseManager.TestAndSaveServerUrl(address, message)
                MessageBox.Show(message, If(ok, "Connected", "Connection Failed"),
                        MessageBoxButton.OK, If(ok, MessageBoxImage.Information, MessageBoxImage.Error))
            Finally
                btnConnectServer.IsEnabled = True
            End Try
        End Sub

        Private Sub btnCancel_Click(sender As Object, e As RoutedEventArgs)
            DialogResult = False
            Close()
        End Sub
    End Class
End Namespace
