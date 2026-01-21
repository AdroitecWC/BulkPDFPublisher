Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Windows
Imports System.Windows.Input
Imports System.Windows.Media
Imports Ookii.Dialogs.Wpf
Imports Microsoft.Win32

Public Class SettingsWindowViewModel
    Implements INotifyPropertyChanged

    ' -------------------------------
    ' Creo Paths
    ' -------------------------------
    Private _creoPath As String
    Private _originalCreoPath As String
    Public Property CreoPath As String
        Get
            Return _creoPath
        End Get
        Set(value As String)
            If _creoPath <> value Then
                _creoPath = value
                OnPropertyChanged()
            End If
        End Set
    End Property

    Private _workingDirectory As String
    Private _originalWorkingDirectory As String
    Public Property WorkingDirectory As String
        Get
            Return _workingDirectory
        End Get
        Set(value As String)
            If _workingDirectory <> value Then
                _workingDirectory = value
                OnPropertyChanged()
            End If
        End Set
    End Property

    ' -------------------------------
    ' Watermark Properties
    ' -------------------------------
    Private _watermarkText As String
    Public Property WatermarkText As String
        Get
            Return _watermarkText
        End Get
        Set(value As String)
            If _watermarkText <> value Then
                _watermarkText = value
                OnPropertyChanged()
            End If
        End Set
    End Property

    Private _watermarkOpacity As Double
    Public Property WatermarkOpacity As Double
        Get
            Return _watermarkOpacity
        End Get
        Set(value As Double)
            Dim clamped As Double = Math.Max(0.0, Math.Min(1.0, value))
            If _watermarkOpacity <> clamped Then
                _watermarkOpacity = clamped
                OnPropertyChanged()
            End If
        End Set
    End Property

    Private _watermarkFontSize As Double
    Public Property WatermarkFontSize As Double
        Get
            Return _watermarkFontSize
        End Get
        Set(value As Double)
            If _watermarkFontSize <> value Then
                _watermarkFontSize = value
                OnPropertyChanged()
            End If
        End Set
    End Property

    Private _watermarkColor As Color
    Public Property WatermarkColor As Color
        Get
            Return _watermarkColor
        End Get
        Set(value As Color)
            If _watermarkColor <> value Then
                _watermarkColor = value
                OnPropertyChanged()
            End If
        End Set
    End Property

    Private _watermarkPosition As String
    Public Property WatermarkPosition As String
        Get
            Return _watermarkPosition
        End Get
        Set(value As String)
            If _watermarkPosition <> value Then
                _watermarkPosition = value
                OnPropertyChanged()
            End If
        End Set
    End Property

    Public ReadOnly Property WatermarkPositions As List(Of String)
        Get
            Return New List(Of String) From {
            "Diagonal",
            "Top-Left",
            "Top-Right",
            "Bottom-Left",
            "Bottom-Right",
            "Center"
        }
        End Get
    End Property

    Private _enableWatermark As Boolean
    Public Property EnableWatermark As Boolean
        Get
            Return _enableWatermark
        End Get
        Set(value As Boolean)
            If _enableWatermark <> value Then
                _enableWatermark = value
                OnPropertyChanged()
            End If
        End Set
    End Property
    ' -------------------------------
    ' License Info (Read-only)
    ' -------------------------------
    Private _licenseStatus As String = "Checking..."

    Public ReadOnly Property LicenseStatus As String
        Get
            Return _licenseStatus
        End Get
    End Property


    Public ReadOnly Property LicenseSystemName As String
        Get
            Return $"System Name: {Environment.MachineName}"
        End Get
    End Property
    Public ReadOnly Property LicenseExpiry As String
        Get
            If LicenseManager.CheckLicense() Then
                Return "Valid until " & LicenseManager.GetActiveLicenseExpiry()
            End If
            Return "-"
        End Get
    End Property



    ' -------------------------------
    ' Commands
    ' -------------------------------
    Public Property BrowseCommand As ICommand
    Public Property BrowseWorkingDirectoryCommand As ICommand
    Public Property SaveCommand As ICommand
    Public Property CancelCommand As ICommand
    Public Property ConfirmCommand As ICommand
    Public Property DeactivateLicenseCommand As ICommand
    Public ReadOnly Property TransferLicenseCommand As ICommand

    ' -------------------------------
    ' Close event
    ' -------------------------------
    Public Event RequestClose As Action

    Public Sub New()
        ' Load settings from SettingsManager
        _creoPath = SettingsManager.Instance.CreoPath
        _originalCreoPath = _creoPath

        _workingDirectory = SettingsManager.Instance.WorkingDirectory
        _originalWorkingDirectory = _workingDirectory

        _watermarkText = SettingsManager.Instance.WatermarkText
        _watermarkOpacity = SettingsManager.Instance.WatermarkOpacity
        _watermarkFontSize = SettingsManager.Instance.WatermarkFontSize
        _watermarkColor = SettingsManager.Instance.WatermarkColor
        _watermarkPosition = SettingsManager.Instance.WatermarkPosition
        _enableWatermark = SettingsManager.Instance.EnableWatermark

        ' Initialize commands
        BrowseCommand = New RelayCommand(AddressOf Browse)
        BrowseWorkingDirectoryCommand = New RelayCommand(AddressOf BrowseWorkingDirectory)
        SaveCommand = New RelayCommand(AddressOf Save)
        CancelCommand = New RelayCommand(AddressOf Cancel)
        ConfirmCommand = New RelayCommand(AddressOf Confirm)
        DeactivateLicenseCommand = New RelayCommand(AddressOf DeactivateLicense)
        TransferLicenseCommand = New RelayCommand(AddressOf ExecuteTransferLicense, AddressOf CanTransferLicense)
        RefreshLicenseInfo()

    End Sub

    ' -------------------------------
    ' Browse Creo exe
    ' -------------------------------
    Private Sub Browse()
        Dim dlg As New OpenFileDialog()
        dlg.Filter = "Creo Executable (parametric.exe)|parametric.exe"
        dlg.Title = "Select Creo parametric.exe"
        dlg.InitialDirectory = "C:\Program Files\PTC"

        If dlg.ShowDialog() = True Then
            CreoPath = dlg.FileName.Trim().Trim(""""c)
        End If
    End Sub

    ' -------------------------------
    ' Browse Working Directory
    ' -------------------------------
    Private Sub BrowseWorkingDirectory()
        Dim dialog As New VistaFolderBrowserDialog()
        dialog.Description = "Select Working Directory"
        dialog.UseDescriptionForTitle = True
        dialog.ShowNewFolderButton = True
        dialog.SelectedPath = WorkingDirectory

        If dialog.ShowDialog() = True Then
            WorkingDirectory = dialog.SelectedPath.Trim().Trim(""""c)
        End If
    End Sub

    ' -------------------------------
    ' Save settings without closing
    ' -------------------------------
    Private Sub Save()
        If Not ValidateInputs() Then Return

        SaveSettings()
        MessageBox.Show("Settings saved successfully.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information)
    End Sub

    ' -------------------------------
    ' Confirm (Save + Close)
    ' -------------------------------
    Private Sub Confirm()
        If Not ValidateInputs() Then Return

        SaveSettings()
        RaiseEvent RequestClose()
    End Sub

    ' -------------------------------
    ' Save logic
    ' -------------------------------
    Private Sub SaveSettings()
        SettingsManager.Instance.CreoPath = CreoPath
        SettingsManager.Instance.WorkingDirectory = WorkingDirectory
        SettingsManager.Instance.WatermarkText = WatermarkText
        SettingsManager.Instance.WatermarkOpacity = WatermarkOpacity
        SettingsManager.Instance.WatermarkFontSize = WatermarkFontSize
        SettingsManager.Instance.WatermarkColor = WatermarkColor
        SettingsManager.Instance.WatermarkPosition = WatermarkPosition
        SettingsManager.Instance.EnableWatermark = EnableWatermark

        ' 👇 persist to disk
        SettingsManager.Instance.Save()

        _originalCreoPath = CreoPath
        _originalWorkingDirectory = WorkingDirectory
    End Sub

    ' -------------------------------
    ' Cancel (restore old values)
    ' -------------------------------
    Private Sub Cancel()
        CreoPath = _originalCreoPath
        WorkingDirectory = _originalWorkingDirectory
        RaiseEvent RequestClose()
    End Sub

    Private Sub DeactivateLicense()
        Dim result = MessageBox.Show(
        "Are you sure you want to deactivate this license?",
        "Confirm Deactivation",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning)

        If result <> MessageBoxResult.Yes Then Return

        Try
            LicenseManager.DeactivateLicense()

            MessageBox.Show(
            "License deactivated successfully. Please restart the application.",
            "Deactivated",
            MessageBoxButton.OK,
            MessageBoxImage.Information)

            Application.Current.Shutdown()

        Catch ex As Exception
            MessageBox.Show(
            "Failed to deactivate license.",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub RefreshLicenseInfo()
        If Not LicenseState.IsChecked Then
            _licenseStatus = "Checking..."
        ElseIf LicenseState.IsValid Then
            _licenseStatus = "License Status: ACTIVE"
        Else
            _licenseStatus = "License Status: INACTIVE"
        End If

        OnPropertyChanged(NameOf(LicenseStatus))
        OnPropertyChanged(NameOf(LicenseExpiry))
    End Sub


    Private Sub ExecuteTransferLicense()

        ' 1. Ask for target system code
        Dim input = Microsoft.VisualBasic.Interaction.InputBox(
        "Enter target system code:",
        "Transfer License"
    )

        If String.IsNullOrWhiteSpace(input) Then Return

        ' 2. Confirm transfer
        Dim confirm = MessageBox.Show(
        "This will deactivate the license on this system and generate a license for another system." &
        Environment.NewLine & Environment.NewLine &
        "Do you want to continue?",
        "Confirm Transfer",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning
    )

        If confirm <> MessageBoxResult.Yes Then Return

        ' 3. Ask user where to save the transferred license
        Dim sfd As New Microsoft.Win32.SaveFileDialog With {
        .Title = "Save Transferred License",
        .Filter = "License Files (*.lic)|*.lic",
        .FileName = "Transferred_License.lic"
    }

        If sfd.ShowDialog() <> True Then Return

        Dim savePath As String = sfd.FileName

        ' 4. Perform transfer
        Try
            Dim generatedPath As String =
            LicenseManager.TransferLicense(input.Trim(), savePath)

            MessageBox.Show(
            "License transferred successfully." & Environment.NewLine & Environment.NewLine &
            "New license generated at:" & Environment.NewLine &
            generatedPath & Environment.NewLine & Environment.NewLine &
            "This system is now unlicensed.",
            "Transfer Complete",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        )

            ' 5. Exit application (mandatory after transfer)
            Application.Current.Shutdown()

        Catch ex As Exception
            MessageBox.Show(
            ex.Message,
            "Transfer Failed",
            MessageBoxButton.OK,
            MessageBoxImage.Error
        )
        End Try

    End Sub

    Private Function ValidateInputs() As Boolean
        If String.IsNullOrWhiteSpace(CreoPath) OrElse Not File.Exists(CreoPath) OrElse Not CreoPath.ToLower().EndsWith("parametric.exe") Then
            MessageBox.Show("Invalid file. Please select a valid parametric.exe.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
            Return False
        End If

        If String.IsNullOrWhiteSpace(WorkingDirectory) OrElse Not Directory.Exists(WorkingDirectory) Then
            MessageBox.Show("Please select a valid working directory.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
            Return False
        End If

        Return True
    End Function
    Private Function CanTransferLicense() As Boolean
        Return LicenseManager.CheckLicense()
    End Function

    ' -------------------------------
    ' INotifyPropertyChanged
    ' -------------------------------
    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
    Protected Sub OnPropertyChanged(<CallerMemberName> Optional name As String = "")
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(name))
    End Sub
End Class
