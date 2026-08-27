Imports System.IO
Imports System.Windows.Media
Imports iTextSharp.text
Imports iTextSharp.text.pdf


Public Class WatermarkHelper

    ''' <summary>
    ''' Adds watermark text to a PDF with specified properties and position.
    ''' </summary>
    Public Shared Sub AddWatermarkToPdf(inputPath As String,
                                        outputPath As String,
                                        watermarkText As String,
                                        opacity As Single,
                                        fontSize As Single,
                                        rotation As Single,
                                        watermarkColor As Color,
                                        position As String)

        Dim reader As New PdfReader(inputPath)
        Using fs As New FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None)
            Dim stamper As New PdfStamper(reader, fs)
            Dim gstate As New PdfGState With {.FillOpacity = opacity}

            Dim bf As BaseFont = BaseFont.CreateFont(BaseFont.HELVETICA_BOLD, BaseFont.CP1252, BaseFont.NOT_EMBEDDED)

            For i As Integer = 1 To reader.NumberOfPages
                Dim pageRect As Rectangle = reader.GetPageSizeWithRotation(i)
                Dim overContent As PdfContentByte = stamper.GetOverContent(i)
                overContent.SaveState()
                overContent.SetGState(gstate)
                overContent.SetColorFill(New BaseColor(watermarkColor.R, watermarkColor.G, watermarkColor.B))
                overContent.SetFontAndSize(bf, fontSize)

                Dim x As Single = 0
                Dim y As Single = 0
                Dim angle As Single = rotation
                Dim align As Integer = Element.ALIGN_LEFT

                Select Case position.ToLower().Replace(" ", "").Replace("-", "")
                    Case "diagonal"
                        ' place watermark across the whole page diagonally
                        x = pageRect.Width / 2
                        y = pageRect.Height / 2
                        align = Element.ALIGN_CENTER
                        angle = 45
                        ' use ColumnText so it spans nicely
                        ColumnText.ShowTextAligned(overContent, align, New Phrase(watermarkText, New Font(bf, fontSize, Font.BOLD, New BaseColor(watermarkColor.R, watermarkColor.G, watermarkColor.B))), x, y, angle)
                    Case "topleft"
                        x = 40
                        y = pageRect.Height - fontSize - 40
                        angle = 0
                        overContent.ShowTextAligned(Element.ALIGN_LEFT, watermarkText, x, y, angle)


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
                        ColumnText.ShowTextAligned(overContent, align, New Phrase(watermarkText, New Font(bf, fontSize, Font.BOLD, New BaseColor(watermarkColor.R, watermarkColor.G, watermarkColor.B))), x, y, angle)
                End Select

                overContent.RestoreState()
            Next

            stamper.Close()
        End Using
        reader.Close()
    End Sub
End Class