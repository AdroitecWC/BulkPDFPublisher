Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Windows
Imports Microsoft.Win32
Imports Ookii.Dialogs.Wpf

Public Class SettingsWindowViewModel
    Implements INotifyPropertyChanged

    ' Creo executable path
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

    ' Working directory path
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

    ' Preferred report viewer
    Private _preferredReportViewer As String
    Public Property PreferredReportViewer As String
        Get
            Return _preferredReportViewer
        End Get
        Set(value As String)
            If _preferredReportViewer <> value Then
                _preferredReportViewer = value
                OnPropertyChanged()
            End If
        End Set
    End Property

    ' Commands
    Public Property BrowseCommand As ICommand
    Public Property BrowseWorkingDirectoryCommand As ICommand
    Public Property SaveCommand As ICommand
    Public Property CancelCommand As ICommand
    Public Property ConfirmCommand As ICommand

    ' Close event
    Public Event RequestClose As Action

    Public Sub New()
        ' Load settings
        _creoPath = My.Settings.CreoPath
        _originalCreoPath = _creoPath

        _workingDirectory = My.Settings.WorkingDirectory
        _originalWorkingDirectory = _workingDirectory



        ' Initialize commands
        BrowseCommand = New RelayCommand(AddressOf Browse)
        BrowseWorkingDirectoryCommand = New RelayCommand(AddressOf BrowseWorkingDirectory)
        SaveCommand = New RelayCommand(AddressOf Save)
        CancelCommand = New RelayCommand(AddressOf Cancel)
        ConfirmCommand = New RelayCommand(AddressOf Confirm)
    End Sub

    ' Browse for parametric.exe
    Private Sub Browse()
        Dim dlg As New OpenFileDialog()
        dlg.Filter = "Creo Executable (parametric.exe)|parametric.exe"
        dlg.Title = "Select Creo parametric.exe"
        dlg.InitialDirectory = "C:\Program Files\PTC"

        If dlg.ShowDialog() = True Then
            CreoPath = dlg.FileName.Trim().Trim(""""c)
        End If
    End Sub

    ' Browse for working directory
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

    ' Save settings without closing
    Private Sub Save()
        If Not ValidateInputs() Then Return

        ' Save to My.Settings
        My.Settings.CreoPath = CreoPath
        My.Settings.WorkingDirectory = WorkingDirectory

        My.Settings.Save()

        ' Update backup
        _originalCreoPath = CreoPath
        _originalWorkingDirectory = WorkingDirectory

        MessageBox.Show("Settings saved successfully.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information)
    End Sub

    ' Cancel changes
    Private Sub Cancel()
        CreoPath = _originalCreoPath
        WorkingDirectory = _originalWorkingDirectory
        PreferredReportViewer = If(My.Settings.PreferredReportViewer, "Default")
        RaiseEvent RequestClose()
    End Sub

    ' Save and close
    Private Sub Confirm()
        If Not ValidateInputs() Then Return

        My.Settings.CreoPath = CreoPath
        My.Settings.WorkingDirectory = WorkingDirectory

        My.Settings.Save()

        RaiseEvent RequestClose()
    End Sub

    ' Validation
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

    ' INotifyPropertyChanged
    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
    Protected Sub OnPropertyChanged(<CallerMemberName> Optional name As String = "")
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(name))
    End Sub
End Class
