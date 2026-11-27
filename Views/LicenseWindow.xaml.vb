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

        Private Sub btnActivate_Click(sender As Object, e As RoutedEventArgs)
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
                LicenseManager.ActivateLicense()

                Dim result = LicenseManager.CheckLicenseDetailed()

                Select Case result
                    Case LicenseManager.LicenseError.None
                        MessageBox.Show("License Activated!", "Success", MessageBoxButton.OK, MessageBoxImage.Information)
                        DialogResult = True
                        Close()

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

        Private Sub btnCancel_Click(sender As Object, e As RoutedEventArgs)
            DialogResult = False
            Close()
        End Sub
    End Class
End Namespace
