Imports System.IO
Imports Microsoft.Win32
Imports System.Windows

Namespace CreoPublisherApp
    Partial Public Class LicenseWindow
        Inherits Window

        Private ReadOnly requestCode As String = LicenseManager.GenerateLicenseCode()

        Public Sub New()
            InitializeComponent()

            txtSystemName.Text = Environment.MachineName
            txtRequestCode.Text = requestCode

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
                MessageBox.Show("License file not found.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error)
                Return
            End If

            Try
                Dim targetPath = LicenseManager.GetLicensePath()

                ' Copy license to app location
                File.Copy(selectedFile, targetPath, True)

                ' 🔑 ACTIVATE FIRST (this was missing)
                If Not LicenseManager.ActivateLicense() Then
                    MessageBox.Show("License activation failed.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error)
                    Return
                End If

                ' Optional safety check
                If LicenseManager.CheckLicense() Then
                    MessageBox.Show("License Activated!", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information)
                    DialogResult = True
                    Close()
                Else
                    MessageBox.Show("License activation verification failed.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error)
                End If

            Catch ex As Exception
                MessageBox.Show("Activation failed: " & ex.Message,
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End Sub

        Private Sub btnCancel_Click(sender As Object, e As RoutedEventArgs)
            DialogResult = False
            Close()
        End Sub
    End Class
End Namespace
