Imports System.IO
Imports Microsoft.Win32
Imports System.Windows

Namespace CreoPublisherApp

    Partial Public Class LicenseWindow
        Inherits Window

        Private ReadOnly requestCode As String = LicenseManager.GenerateLicenseCode()

        Public Sub New()
            InitializeComponent()

            ' Populate system info
            txtSystemName.Text = Environment.MachineName
            txtRequestCode.Text = requestCode

            ' Load existing license info (if any)
            LoadLicenseInfo()
        End Sub

        ' =====================================================
        ' Load License Info (Activation + Details tab)
        ' =====================================================
        Private Sub LoadLicenseInfo()

            If LicenseManager.CheckLicense() Then

                txtLicStatus.Text = "ACTIVE"

                Try
                    txtLicExpiry.Text = LicenseManager.GetActiveLicenseExpiry()
                Catch
                    txtLicExpiry.Text = "Unknown"
                End Try

                txtLicSystemCode.Text = requestCode

            Else
                txtLicStatus.Text = "NOT ACTIVATED"
                txtLicExpiry.Text = "-"
                txtLicSystemCode.Text = requestCode
            End If

        End Sub

        ' =====================================================
        ' Browse License File
        ' =====================================================
        Private Sub btnBrowse_Click(sender As Object, e As RoutedEventArgs)
            Dim ofd As New OpenFileDialog With {
                .Filter = "License Files (*.lic)|*.lic"
            }

            If ofd.ShowDialog() = True Then
                txtFilePath.Text = ofd.FileName
            End If
        End Sub

        ' =====================================================
        ' Activate License
        ' =====================================================
        Private Sub btnActivate_Click(sender As Object, e As RoutedEventArgs)

            Dim selectedFile = txtFilePath.Text.Trim()

            If Not File.Exists(selectedFile) Then
                MessageBox.Show("Please select a valid license file.",
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error)
                Return
            End If

            Try
                If LicenseManager.InstallLicense(selectedFile) Then

                    If LicenseManager.CheckLicense() Then
                        MessageBox.Show("License activated successfully.",
                                        "Success",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information)

                        LoadLicenseInfo()
                    Else
                        MessageBox.Show("Invalid license.",
                                        "Activation Failed",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Error)
                    End If

                Else
                    MessageBox.Show("License installation failed.",
                                    "Error",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error)
                End If

            Catch ex As Exception
                MessageBox.Show("Activation failed: " & ex.Message,
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error)
            End Try

        End Sub

        ' =====================================================
        ' Deactivate / Return License
        ' =====================================================
        Private Sub btnDeactivate_Click(sender As Object, e As RoutedEventArgs)

            If Not LicenseState.IsValid Then
                MessageBox.Show("License inactive. Please activate.")
                Return
            End If


            Dim result = MessageBox.Show(
                "Are you sure you want to deactivate this license?" & vbCrLf &
                "You will need to reactivate it to use the software again.",
                "Confirm Deactivation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning)

            If result <> MessageBoxResult.Yes Then Return

            Try
                If LicenseManager.ReturnLicense() Then

                    MessageBox.Show("License deactivated successfully.",
                                    "Success",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information)

                    txtFilePath.Text = ""

                    txtLicStatus.Text = "DEACTIVATED"
                    txtLicExpiry.Text = "-"
                    txtLicSystemCode.Text = requestCode

                Else
                    MessageBox.Show("Failed to deactivate license.",
                                    "Error",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error)
                End If

            Catch ex As Exception
                MessageBox.Show("Deactivation failed: " & ex.Message,
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error)
            End Try

        End Sub

    End Class
End Namespace
