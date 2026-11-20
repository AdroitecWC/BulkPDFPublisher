Imports CreoPublisherApp.CreoPublisherApp
Imports System.IO
Imports System.Windows

Partial Class Application
    Inherits System.Windows.Application

    Protected Overrides Sub OnStartup(e As StartupEventArgs)
        ' Registered first so a crash anywhere below (CLI mode, license check, GUI)
        ' gets logged instead of just exiting with the opaque CLR exit code.
        AddHandler DispatcherUnhandledException, AddressOf OnDispatcherUnhandledException
        AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException
        AddHandler System.Threading.Tasks.TaskScheduler.UnobservedTaskException, AddressOf OnUnobservedTaskException

        MyBase.OnStartup(e)

        Dim args = e.Args

        ' CLI Mode: Expect exactly 3 arguments
        If args.Length = 3 Then
            Dim inputPath = args(0)
            Dim outputPath = args(1)
            Dim creoPath = args(2)

            ' Validate paths
            If Not Directory.Exists(inputPath) Then
                MessageBox.Show("Invalid input folder path.")
                Environment.Exit(1)
            End If

            If Not File.Exists(creoPath) OrElse Not creoPath.ToLower().EndsWith("parametric.exe") Then
                MessageBox.Show("Invalid Creo executable path.")
                Environment.Exit(1)
            End If

            If Not Directory.Exists(outputPath) Then
                Directory.CreateDirectory(outputPath)
            End If

            ' Save Creo path to settings
            My.Settings.CreoPath = creoPath
            My.Settings.Save()

            ' Run publishing
            Dim vm As New ViewModels.MainViewModel()

            ' ✅ Manually load .drw files since no UI is shown
            Dim drwFiles = Directory.GetFiles(inputPath, "*.drw", SearchOption.TopDirectoryOnly)
            For Each file In drwFiles
                Dim fileModel As New FileItemModel(file)
                fileModel.IsSelected = True
                vm.Files.Add(fileModel)
            Next

            vm.OutputPath = outputPath

            vm.PublishFiles()

            Environment.Exit(0)
        Else
            ' GUI Mode

            ' Default ShutdownMode is OnLastWindowClose — with no window open yet,
            ' closing LicenseWindow (the only open window at that point) would
            ' trigger shutdown immediately, racing with intro.Show() right after
            ' it and closing the whole app instead of continuing on.
            ShutdownMode = ShutdownMode.OnExplicitShutdown

            Dim licErr = LicenseManager.CheckLicenseDetailed()

            Select Case licErr
                Case LicenseManager.LicenseError.None
                    ' Valid — fall through to main window

                Case LicenseManager.LicenseError.Expired
                    ' No blocking dialog -- go straight to the activation window so
                    ' loading a renewed license is one step, not an OK click first.
                    If Not TryActivateViaLicenseWindow() Then
                        Environment.Exit(0)
                    End If

                Case LicenseManager.LicenseError.WrongProduct
                    MessageBox.Show(
                        "This license was issued for a different Adroitec product." & vbCrLf &
                        "Please use the license generated for " & LicenseManager.AppProduct & "." & vbCrLf &
                        "Click OK to load the correct license.",
                        "Wrong Product",
                        MessageBoxButton.OK, MessageBoxImage.Error)
                    If Not TryActivateViaLicenseWindow() Then
                        Environment.Exit(0)
                    End If

                Case Else
                    ' Missing file, not activated, machine mismatch, tampered, transferred --
                    ' show the activation window directly.
                    If Not TryActivateViaLicenseWindow() Then
                        Environment.Exit(0)
                    End If
            End Select

            ShutdownMode = ShutdownMode.OnLastWindowClose
            Dim intro = New IntroWindow()
            intro.Show()
        End If
    End Sub

    Private Function TryActivateViaLicenseWindow() As Boolean
        Dim lic As New LicenseWindow()
        Return lic.ShowDialog() = True
    End Function

    Private Sub OnDispatcherUnhandledException(sender As Object, e As System.Windows.Threading.DispatcherUnhandledExceptionEventArgs)
        ErrorLogger.LogException("Unhandled dispatcher exception", e.Exception)
        MessageBox.Show(
            "An unexpected error occurred and the application must close." & vbCrLf & vbCrLf &
            "Details written to:" & vbCrLf & ErrorLogger.LogFilePath & vbCrLf & vbCrLf &
            ErrorLogger.FormatBrief(e.Exception),
            "Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Error)
    End Sub

    Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        ErrorLogger.LogException("Unhandled AppDomain exception", TryCast(e.ExceptionObject, Exception))
    End Sub

    Private Sub OnUnobservedTaskException(sender As Object, e As System.Threading.Tasks.UnobservedTaskExceptionEventArgs)
        ErrorLogger.LogException("Unobserved task exception", e.Exception)
        e.SetObserved()
    End Sub

End Class
