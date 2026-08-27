Public Class SettingsManager
    Private Shared _instance As SettingsManager

    Public Property CreoPath As String
    Public Property WorkingDirectory As String
    ' New Watermark Settings
    Public Property WatermarkText As String
    Public Property WatermarkOpacity As Double
    Public Property WatermarkFontSize As Double
    Public Property WatermarkColor As Color
    Public Property WatermarkPosition As String

    Public Property EnableWatermark As Boolean = False

    Private Sub New()
        CreoPath = My.Settings.CreoPath
        WorkingDirectory = My.Settings.WorkingDirectory

        WatermarkText = My.Settings.WatermarkText
        WatermarkOpacity = My.Settings.WatermarkOpacity
        WatermarkFontSize = My.Settings.WatermarkFontSize
        WatermarkColor = ColorConverter.ConvertFromString(My.Settings.WatermarkColor)
        WatermarkPosition = My.Settings.WatermarkPosition
        EnableWatermark = My.Settings.EnableWatermark
    End Sub



    Public Shared ReadOnly Property Instance As SettingsManager
        Get
            If _instance Is Nothing Then
                _instance = New SettingsManager()
            End If
            Return _instance
        End Get
    End Property
    Public Sub Save()
        My.Settings.CreoPath = CreoPath
        My.Settings.WorkingDirectory = WorkingDirectory
        My.Settings.WatermarkText = WatermarkText
        My.Settings.WatermarkOpacity = WatermarkOpacity
        My.Settings.WatermarkFontSize = WatermarkFontSize
        My.Settings.WatermarkColor = WatermarkColor.ToString()

        My.Settings.WatermarkPosition = WatermarkPosition
        My.Settings.EnableWatermark = EnableWatermark

        My.Settings.Save()
    End Sub
End Class
