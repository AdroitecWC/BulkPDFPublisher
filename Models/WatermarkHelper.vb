Imports System.IO
Imports System.Windows.Media
Imports iTextSharp.text
Imports iTextSharp.text.pdf

Public Class WatermarkHelper

    ''' <summary>
    ''' Adds watermark text to a PDF. Writes to outputPath. If overwriteOriginal = True, it will write to a temp file then replace inputPath.
    ''' </summary>
    Public Shared Sub AddWatermarkToPdf(inputPath As String,
                                        outputPath As String,
                                        watermarkText As String,
                                        opacity As Single,
                                        fontSize As Single,
                                        rotation As Single,
                                        watermarkColor As Color,
                                        position As String)

        If Not File.Exists(inputPath) Then
            Throw New FileNotFoundException("Input PDF not found", inputPath)
        End If

        ' Normalize position string for easier matching
        Dim posKey = If(position, "").ToLower().Replace(" ", "").Replace("-", "")

        ' Ensure opacity in 0..1
        If Single.IsNaN(opacity) Then opacity = 1.0F
        opacity = Math.Max(0.0F, Math.Min(1.0F, opacity))

        ' Convert WPF Color (A,R,G,B) to iText BaseColor (R,G,B)
        Dim baseCol = New BaseColor(watermarkColor.R, watermarkColor.G, watermarkColor.B)

        ' Use a temp file as output to avoid partial overwrite if anything fails
        Dim tempOutput = outputPath
        Dim overwriteOriginal As Boolean = False
        If String.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase) Then
            tempOutput = Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath) & "_tmp_" & Guid.NewGuid().ToString("N") & ".pdf")
            overwriteOriginal = True
        End If

        Dim reader As PdfReader = Nothing
        Dim stamper As PdfStamper = Nothing
        Try
            reader = New PdfReader(inputPath)
            Using fs As New FileStream(tempOutput, FileMode.Create, FileAccess.Write, FileShare.None)
                stamper = New PdfStamper(reader, fs)

                Dim gstate As New PdfGState With {.FillOpacity = opacity, .StrokeOpacity = opacity}
                Dim bf As BaseFont = BaseFont.CreateFont(BaseFont.HELVETICA_BOLD, BaseFont.CP1252, BaseFont.NOT_EMBEDDED)

                For i As Integer = 1 To reader.NumberOfPages
                    Dim pageRect As Rectangle = reader.GetPageSizeWithRotation(i)
                    Dim overContent As PdfContentByte = stamper.GetOverContent(i)

                    overContent.SaveState()
                    overContent.SetGState(gstate)
                    overContent.SetColorFill(baseCol)
                    overContent.SetFontAndSize(bf, fontSize)

                    Dim x As Single = 0
                    Dim y As Single = 0
                    Dim angle As Single = rotation
                    Dim align As Integer = Element.ALIGN_LEFT

                    Select Case posKey
                        Case "diagonal"
                            x = pageRect.Width / 2
                            y = pageRect.Height / 2
                            align = Element.ALIGN_CENTER
                            angle = 45
                            ColumnText.ShowTextAligned(overContent, align,
                                                      New Phrase(watermarkText, New Font(bf, fontSize, Font.BOLD, baseCol)),
                                                      x, y, angle)
                        Case "topleft"
                            x = 40
                            y = pageRect.Height - fontSize - 40
                            angle = 0
                            overContent.ShowTextAligned(Element.ALIGN_LEFT, watermarkText, x, y, angle)
                        Case "topright"
                            x = pageRect.Width - bf.GetWidthPoint(watermarkText, fontSize) - 40
                            y = pageRect.Height - fontSize - 40
                            angle = 0
                            overContent.ShowTextAligned(Element.ALIGN_LEFT, watermarkText, x, y, angle)
                        Case "bottomleft"
                            x = 40
                            y = 40
                            angle = 0
                            overContent.ShowTextAligned(Element.ALIGN_LEFT, watermarkText, x, y, angle)
                        Case "bottomright"
                            x = pageRect.Width - bf.GetWidthPoint(watermarkText, fontSize) - 40
                            y = 40
                            angle = 0
                            overContent.ShowTextAligned(Element.ALIGN_LEFT, watermarkText, x, y, angle)
                        Case "center"
                            x = pageRect.Width / 2
                            y = pageRect.Height / 2
                            angle = 0
                            overContent.ShowTextAligned(Element.ALIGN_CENTER, watermarkText, x, y, angle)
                        Case Else
                            ' fallback → Diagonal
                            x = pageRect.Width / 2
                            y = pageRect.Height / 2
                            align = Element.ALIGN_CENTER
                            angle = 45
                            ColumnText.ShowTextAligned(overContent, align,
                                                      New Phrase(watermarkText, New Font(bf, fontSize, Font.BOLD, baseCol)),
                                                      x, y, angle)
                    End Select

                    overContent.RestoreState()
                Next

                stamper.Close()
            End Using
        Finally
            If stamper IsNot Nothing Then
                Try
                    stamper.Close()
                Catch
                End Try
            End If
            If reader IsNot Nothing Then
                reader.Close()
            End If
        End Try

        ' Replace original if requested
        If overwriteOriginal Then
            Try
                File.Delete(inputPath)
                File.Move(tempOutput, outputPath)
            Catch ex As Exception
                ' If replace fails, leave temp file for manual inspection and rethrow
                Throw
            End Try
        End If
    End Sub
End Class
