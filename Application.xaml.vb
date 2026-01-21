Imports CreoPublisherApp.CreoPublisherApp
Imports System.IO
Imports System.Windows
Imports System.Threading.Tasks

Partial Class Application
    Inherits System.Windows.Application

    Protected Overrides Sub OnStartup(e As StartupEventArgs)
        MyBase.OnStartup(e)

        ' -------------------------------------------------
        ' 🔹 START LICENSE CHECK IN BACKGROUND (ONCE)
        ' -------------------------------------------------
        Task.Run(Sub()
                     Try
                         Dim valid = LicenseManager.CheckLicense()
                         LicenseState.IsValid = valid

                         If valid Then
                             LicenseState.ExpiryDate = LicenseManager.GetActiveLicenseExpiry()
                         End If
                     Catch
                         LicenseState.IsValid = False
                     Finally
                         LicenseState.IsChecked = True
                     End Try
                 End Sub)

        ' -------------------------------------------------
        ' 🔹 EXISTING LOGIC (UNCHANGED)
        ' -------------------------------------------------
        Dim args = e.Args

        ' CLI Mode
        If args.Length = 3 Then
            Dim inputPath = args(0)
            Dim outputPath = args(1)
            Dim creoPath = args(2)

            If Not Directory.Exists(inputPath) Then
                MessageBox.Show("Invalid input folder path.")
                Environment.Exit(1)
            End If

            If Not File.Exists(creoPath) OrElse
               Not creoPath.ToLower().EndsWith("parametric.exe") Then
                MessageBox.Show("Invalid Creo executable path.")
                Environment.Exit(1)
            End If

            If Not Directory.Exists(outputPath) Then
                Directory.CreateDirectory(outputPath)
            End If

            My.Settings.CreoPath = creoPath
            My.Settings.Save()

            Dim vm As New ViewModels.MainViewModel()

            Dim drwFiles =
                Directory.GetFiles(inputPath, "*.drw", SearchOption.TopDirectoryOnly)

            For Each file In drwFiles
                Dim fileModel As New FileItemModel(file)
                fileModel.IsSelected = True
                vm.Files.Add(fileModel)
            Next

            vm.OutputPath = outputPath
            vm.PublishFiles()

            Environment.Exit(0)

        Else
            Dim waited As Integer = 0
            While Not LicenseState.IsChecked AndAlso waited < 3000
                System.Threading.Thread.Sleep(100)
                waited += 100
            End While
            ' GUI Mode (UNCHANGED for now)
            If Not LicenseManager.CheckLicense() Then
                Dim licWin As New LicenseWindow()
                Dim activated = licWin.ShowDialog()
                If activated <> True Then
                    Environment.Exit(0)
                End If
            End If

            Dim intro = New IntroWindow()
            intro.Show()
        End If
    End Sub

End Class
