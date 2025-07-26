Imports System.ComponentModel
Imports System.Drawing.Printing
Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms
Imports Ookii.Dialogs.Wpf
Imports Microsoft.Win32
Imports OpenFileDialog = Microsoft.Win32.OpenFileDialog

Public Class SettingsWindowViewModel
    Implements INotifyPropertyChanged

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

    Public Property BrowseCommand As ICommand
    Public Property SaveCommand As ICommand
    Public Property CancelCommand As ICommand
    Public Property ConfirmCommand As ICommand
    Public Property BrowseWorkingDirectoryCommand As ICommand

    Public Event RequestClose As Action

    Public Sub New()
        _creoPath = My.Settings.CreoPath
        _originalCreoPath = _creoPath
        BrowseCommand = New RelayCommand(AddressOf Browse)
        SaveCommand = New RelayCommand(AddressOf Save)
        CancelCommand = New RelayCommand(AddressOf Cancel)
        ConfirmCommand = New RelayCommand(AddressOf Confirm)

        _workingDirectory = My.Settings.WorkingDirectory
        _originalWorkingDirectory = _workingDirectory
        BrowseWorkingDirectoryCommand = New RelayCommand(AddressOf BrowseWorkingDirectory)


    End Sub

    Private Sub Browse()
        Dim dlg As New OpenFileDialog()
        dlg.Filter = "Creo Executable (parametric.exe)|parametric.exe"
        dlg.Title = "Select Creo parametric.exe"
        dlg.InitialDirectory = "C:\Program Files\PTC"
        If dlg.ShowDialog() = True Then
            Dim cleanedPath = dlg.FileName.Trim().Trim(""""c)
            CreoPath = cleanedPath
        End If
    End Sub
    Private Sub BrowseWorkingDirectory()
        Dim dialog As New VistaFolderBrowserDialog()
        dialog.Description = "Select Working Directory"
        dialog.UseDescriptionForTitle = True
        dialog.ShowNewFolderButton = True
        dialog.SelectedPath = WorkingDirectory

        If dialog.ShowDialog() = True Then
            Dim cleanedPath = dialog.SelectedPath.Trim().Trim(""""c)
            WorkingDirectory = cleanedPath
        End If
    End Sub

    Private Sub Save()
        ' Validate Creo Path
        If Not File.Exists(CreoPath) OrElse Not CreoPath.ToLower().EndsWith("parametric.exe") Then
            System.Windows.MessageBox.Show("Invalid file. Please select a valid parametric.exe.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
            Return
        End If

        ' Validate Working Directory
        If String.IsNullOrWhiteSpace(WorkingDirectory) OrElse Not Directory.Exists(WorkingDirectory) Then
            System.Windows.MessageBox.Show("Please select a valid working directory.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
            Return
        End If

        ' Save to My.Settings
        My.Settings.CreoPath = CreoPath
        My.Settings.WorkingDirectory = WorkingDirectory
        My.Settings.Save()

        ' Update Singleton (optional)
        SettingsManager.Instance.WorkingDirectory = WorkingDirectory

        ' Update backup values
        _originalCreoPath = CreoPath
        _originalWorkingDirectory = WorkingDirectory

        System.Windows.MessageBox.Show("Settings saved successfully.")
    End Sub

    Private Sub Cancel()
        CreoPath = _originalCreoPath
        WorkingDirectory = _originalWorkingDirectory

        RaiseEvent RequestClose()
    End Sub

    Private Sub Confirm()
        If Not File.Exists(CreoPath) OrElse Not CreoPath.ToLower().EndsWith("parametric.exe") Then
            System.Windows.MessageBox.Show("Invalid file. Please select a valid parametric.exe.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
            Return
        End If
        If String.IsNullOrWhiteSpace(WorkingDirectory) OrElse Not Directory.Exists(WorkingDirectory) Then
            System.Windows.MessageBox.Show("Please select the working directory.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
            Return
        End If

        My.Settings.CreoPath = CreoPath
        SettingsManager.Instance.WorkingDirectory = Me.WorkingDirectory
        My.Settings.Save()
        RaiseEvent RequestClose()
    End Sub

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
    Protected Sub OnPropertyChanged(<CallerMemberName> Optional name As String = "")
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(name))
    End Sub

End Class
