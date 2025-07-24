Public Class SettingsManager
    Private Shared _instance As SettingsManager

    Public Property CreoPath As String
    Public Property WorkingDirectory As String

    Private Sub New()
        CreoPath = String.Empty
        WorkingDirectory = String.Empty
    End Sub

    Public Shared ReadOnly Property Instance As SettingsManager
        Get
            If _instance Is Nothing Then
                _instance = New SettingsManager()
            End If
            Return _instance
        End Get
    End Property
End Class
