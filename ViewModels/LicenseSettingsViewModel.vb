Imports System.ComponentModel
Imports System.Runtime.CompilerServices
Imports System.Windows
Imports System.Windows.Input

Public Class LicenseSettingsViewModel
    Implements INotifyPropertyChanged

    ' -------------------------------
    ' License Action Selection
    ' -------------------------------
    Private _isDeactivateSelected As Boolean = True
    Public Property IsDeactivateSelected As Boolean
        Get
            Return _isDeactivateSelected
        End Get
        Set(value As Boolean)
            If _isDeactivateSelected <> value Then
                _isDeactivateSelected = value

                If value Then
                    _isTransferSelected = False
                    OnPropertyChanged(NameOf(IsTransferSelected))
                End If

                OnPropertyChanged()
                OnPropertyChanged(NameOf(PrimaryLicenseActionText))
            End If
        End Set
    End Property

    Private _isTransferSelected As Boolean
    Public Property IsTransferSelected As Boolean
        Get
            Return _isTransferSelected
        End Get
        Set(value As Boolean)
            If _isTransferSelected <> value Then
                _isTransferSelected = value

                If value Then
                    _isDeactivateSelected = False
                    OnPropertyChanged(NameOf(IsDeactivateSelected))
                End If

                OnPropertyChanged()
                OnPropertyChanged(NameOf(PrimaryLicenseActionText))
            End If
        End Set
    End Property

    Public ReadOnly Property PrimaryLicenseActionText As String
        Get
            If IsTransferSelected Then
                Return "Transfer License"
            End If
            Return "Deactivate License"
        End Get
    End Property

    ' -------------------------------
    ' Commands
    ' -------------------------------
    Public Property DeactivateLicenseCommand As ICommand
    Public Property TransferLicenseCommand As ICommand
    Public Property PrimaryLicenseActionCommand As ICommand
    Public Property CancelCommand As ICommand
    Public Property ConfirmCommand As ICommand

    ' -------------------------------
    ' Events
    ' -------------------------------
    Public Event RequestClose As Action
    Public Event RequestShutdown As Action

    ' -------------------------------
    ' Constructor
    ' -------------------------------
    Public Sub New()
        DeactivateLicenseCommand = New RelayCommand(AddressOf DeactivateLicense)
        TransferLicenseCommand = New RelayCommand(AddressOf TransferLicense)
        PrimaryLicenseActionCommand = New RelayCommand(AddressOf ExecutePrimaryLicenseAction)

        CancelCommand = New RelayCommand(AddressOf Cancel)
        ConfirmCommand = New RelayCommand(AddressOf Confirm)
    End Sub

    ' -------------------------------
    ' Actions
    ' -------------------------------
    Private Sub ExecutePrimaryLicenseAction()

        If IsTransferSelected Then
            TransferLicense()
            Return
        End If

        If IsDeactivateSelected Then
            DeactivateLicense()
            Return
        End If

        MessageBox.Show(
            "Please select a license action.",
            "No Action Selected",
            MessageBoxButton.OK,
            MessageBoxImage.Information)

    End Sub

    Private Sub DeactivateLicense()

        If Not IsDeactivateSelected Then
            MessageBox.Show(
                "Please select 'Deactivate License' option to proceed.",
                "Action Not Selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information)
            Return
        End If

        Dim result = MessageBox.Show(
            "This will permanently deactivate the license and restart the application." &
            Environment.NewLine & Environment.NewLine &
            "The activation window will reopen right away — you'll need a new license to continue using the software." &
            Environment.NewLine & Environment.NewLine &
            "Do you want to continue?",
            "Deactivate License",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning)

        If result <> MessageBoxResult.Yes Then Return

        If LicenseManager.DeactivateLicense() Then
            MessageBox.Show(
                "License deactivated successfully. The application will now restart.",
                "License Deactivated",
                MessageBoxButton.OK,
                MessageBoxImage.Information)

            RaiseEvent RequestShutdown()
        Else
            MessageBox.Show(
                "License deactivation failed or no active license found.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error)
        End If

    End Sub

    Private Sub TransferLicense()

        If Not IsTransferSelected Then
            MessageBox.Show(
                "Please select 'Transfer License' option to proceed.",
                "Action Not Selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information)
            Return
        End If

        Dim vm As New TransferLicenseViewModel()
        Dim win As New CreoPublisherApp.TransferLicenseWindow() With {
            .Owner = Application.Current.MainWindow,
            .DataContext = vm
        }

        win.ShowDialog()

    End Sub

    Private Sub Cancel()
        RaiseEvent RequestClose()
    End Sub

    Private Sub Confirm()
        RaiseEvent RequestClose()
    End Sub

    ' -------------------------------
    ' INotifyPropertyChanged
    ' -------------------------------
    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Protected Sub OnPropertyChanged(<CallerMemberName> Optional name As String = "")
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(name))
    End Sub

End Class
