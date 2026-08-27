Imports System.ComponentModel
Imports System.Runtime.CompilerServices
Imports System.Windows
Imports System.Windows.Input

Public Class TransferLicenseViewModel
    Implements INotifyPropertyChanged

    ' -------------------------------
    ' Properties
    ' -------------------------------
    Private _newSystemCode As String
    Public Property NewSystemCode As String
        Get
            Return _newSystemCode
        End Get
        Set(value As String)
            If _newSystemCode <> value Then
                _newSystemCode = value
                OnPropertyChanged()
            End If
        End Set
    End Property

    ' -------------------------------
    ' Commands
    ' -------------------------------
    Public Property ConfirmCommand As ICommand
    Public Property CancelCommand As ICommand

    ' -------------------------------
    ' Events
    ' -------------------------------
    Public Event RequestClose As Action(Of Boolean)

    Public Sub New()
        ConfirmCommand = New RelayCommand(AddressOf Confirm)
        CancelCommand = New RelayCommand(AddressOf Cancel)
    End Sub

    ' -------------------------------
    ' Command Handlers
    ' -------------------------------
    Private Sub Confirm()

        If String.IsNullOrWhiteSpace(NewSystemCode) Then
            MessageBox.Show(
            "Please enter a valid system code.",
            "Validation Error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning)
            Return
        End If

        ' Ask where to save the transferred license
        Dim dlg As New Microsoft.Win32.SaveFileDialog()
        dlg.Filter = "License File (*.lic)|*.lic"
        dlg.FileName = "License.lic"

        If dlg.ShowDialog() <> True Then
            Return
        End If

        ' Call backend transfer logic
        Dim success = LicenseManager.TransferLicense(
        NewSystemCode.Trim(),
        dlg.FileName)

        If Not success Then
            MessageBox.Show(
            "License transfer failed or this license was already transferred.",
            "Transfer Failed",
            MessageBoxButton.OK,
            MessageBoxImage.Error)
            Return
        End If

        MessageBox.Show(
        "License transferred successfully. The application will now close.",
        "Transfer Complete",
        MessageBoxButton.OK,
        MessageBoxImage.Information)

        ' Close window
        RaiseEvent RequestClose(True)

        ' Shutdown app (ONLY HERE)
        Application.Current.Shutdown()

    End Sub

    Private Sub Cancel()
        RaiseEvent RequestClose(False)
    End Sub

    ' -------------------------------
    ' INotifyPropertyChanged
    ' -------------------------------
    Public Event PropertyChanged As PropertyChangedEventHandler _
        Implements INotifyPropertyChanged.PropertyChanged

    Protected Sub OnPropertyChanged(<CallerMemberName> Optional name As String = "")
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(name))
    End Sub
End Class
