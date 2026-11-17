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

                    ' A floating activation that failed on its first attempt (server down, no
                    ' seats yet, etc.) gets saved and silently retried on every launch -- this
                    ' is the first launch where it actually got a seat, so say so instead of
                    ' jumping straight to the main window with no acknowledgement at all.
                    If LicenseManager.IsActivationPending() Then
                        LicenseManager.ClearActivationPending()
                        MessageBox.Show(
                            "License activated successfully.",
                            "License Activated", MessageBoxButton.OK, MessageBoxImage.Information)
                    End If

                Case LicenseManager.LicenseError.NoSeatsAvailable
                    MessageBox.Show(
                        $"All {LicenseManager.LastSeatsInUse}/{LicenseManager.LastSeatsMax} license seats are currently in use." & vbCrLf &
                        "Please wait for another user to close the application and try again.",
                        "No License Seats Available",
                        MessageBoxButton.OK, MessageBoxImage.Warning)
                    If Not ShouldOpenLicenseWindow(licErr) Then
                        Environment.Exit(0)
                    End If
                    If Not TryActivateViaLicenseWindow() Then
                        Environment.Exit(0)
                    End If

                Case LicenseManager.LicenseError.ServerUnreachable
                    MessageBox.Show(
                        "Cannot reach the license server." & vbCrLf &
                        "Please check your network connection and try again.",
                        "License Server Unavailable",
                        MessageBoxButton.OK, MessageBoxImage.Error)
                    If Not ShouldOpenLicenseWindow(licErr) Then
                        Environment.Exit(0)
                    End If
                    If Not TryActivateViaLicenseWindow() Then
                        Environment.Exit(0)
                    End If

                Case LicenseManager.LicenseError.InvalidLicense
                    MessageBox.Show(
                        "This license is not registered on the license server." & vbCrLf &
                        "Please contact your administrator.",
                        "Invalid License",
                        MessageBoxButton.OK, MessageBoxImage.Error)
                    If Not ShouldOpenLicenseWindow(licErr) Then
                        Environment.Exit(0)
                    End If
                    If Not TryActivateViaLicenseWindow() Then
                        Environment.Exit(0)
                    End If

                Case LicenseManager.LicenseError.ProductNotLicensed, LicenseManager.LicenseError.AmbiguousCustomer
                    ' Activation Code itself was right but the server side isn't ready yet
                    ' (no tranche loaded, or tranches span more than one customer) -- same
                    ' "already configured but can't get a seat right now" situation as the
                    ' cases above, so offer the same choice instead of always reopening the
                    ' activation window with no explanation.
                    If Not ShouldOpenLicenseWindow(licErr) Then
                        Environment.Exit(0)
                    End If
                    If Not TryActivateViaLicenseWindow() Then
                        Environment.Exit(0)
                    End If

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

    ''' <summary>
    ''' A floating license that's already configured but can't get a seat right
    ''' now (server down, no seats free, code not yet provisioned) is a different
    ''' situation from "never activated" -- ask before jumping straight into the
    ''' full activation window, rather than auto-opening it (or silently exiting)
    ''' with no explanation. Returns True if the caller should open LicenseWindow.
    ''' </summary>
    Private Function ShouldOpenLicenseWindow(licErr As LicenseManager.LicenseError) As Boolean
        Dim isFloatingSeatError = licErr = LicenseManager.LicenseError.ServerUnreachable OrElse
                                   licErr = LicenseManager.LicenseError.NoSeatsAvailable OrElse
                                   licErr = LicenseManager.LicenseError.InvalidLicense OrElse
                                   licErr = LicenseManager.LicenseError.ProductNotLicensed OrElse
                                   licErr = LicenseManager.LicenseError.AmbiguousCustomer

        If Not (LicenseManager.IsFloatingConfigured() AndAlso isFloatingSeatError) Then Return True

        Dim answer = MessageBox.Show(
            "Network License is not available." & vbCrLf & vbCrLf &
            "Do you want to change the license?",
            "Network License Unavailable", MessageBoxButton.YesNo, MessageBoxImage.Warning)
        Return answer = MessageBoxResult.Yes
    End Function

    Private Function TryActivateViaLicenseWindow() As Boolean
        Dim lic As New LicenseWindow()
        Return lic.ShowDialog() = True
    End Function

    Protected Overrides Sub OnExit(e As ExitEventArgs)
        LicenseManager.FloatingRelease()
        MyBase.OnExit(e)
    End Sub

    Private Sub OnDispatcherUnhandledException(sender As Object, e As System.Windows.Threading.DispatcherUnhandledExceptionEventArgs)
        LogCrash(e.Exception)
        MessageBox.Show(
            "An unexpected error occurred and the application must close." & vbCrLf & vbCrLf &
            "Details were written to crash.log next to the application." & vbCrLf & vbCrLf &
            e.Exception.Message,
            "Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Error)
    End Sub

    Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        LogCrash(TryCast(e.ExceptionObject, Exception))
    End Sub

    Private Sub OnUnobservedTaskException(sender As Object, e As System.Threading.Tasks.UnobservedTaskExceptionEventArgs)
        LogCrash(e.Exception)
        e.SetObserved()
    End Sub

    Private Shared Sub LogCrash(ex As Exception)
        Try
            Dim logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log")
            Dim text = $"[{DateTime.Now}] {ex?.GetType()?.FullName}: {ex?.Message}" & vbCrLf & ex?.StackTrace & vbCrLf
            If ex IsNot Nothing AndAlso ex.InnerException IsNot Nothing Then
                text &= $"Inner: {ex.InnerException.Message}" & vbCrLf & ex.InnerException.StackTrace & vbCrLf
            End If
            File.AppendAllText(logPath, text & vbCrLf)
        Catch
            ' Logging must never itself crash the crash handler.
        End Try
    End Sub

End Class
