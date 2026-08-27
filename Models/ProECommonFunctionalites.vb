Imports System.IO
Imports System.Text.RegularExpressions
Imports iTextSharp.text
Imports iTextSharp.text.pdf
Imports pfcls

Public Class ProECommonFunctionalites

    Public Sub setConfigOption(configName As String, configValue As String)
        Dim creoSession As IpfcBaseSession = CreoSessionManager.Instance.Session
        creoSession.SetConfigOption(configName, configValue)
    End Sub

    Public Sub ExportAllDrawingsAsPDF(fileItems As List(Of FileItemModel),
                                      outputFolder As String,
                                      isMergeEnabled As Boolean)

        Dim session = CreoSessionManager.Instance.Session
        Dim exportedPdfPaths As New List(Of String)()

        ' Normalize output folder once
        outputFolder = outputFolder.Trim()

        For Each item In fileItems   ' ✅ NO double filtering

            Try
                ' Remove version suffix (.drw.1 → .drw)
                Dim rawName As String = Path.GetFileName(item.FullPath)
                Dim noVersion As String = Regex.Replace(rawName, "\.\d+$", "")
                Dim modelName As String = Path.GetFileNameWithoutExtension(noVersion)

                Dim workingDir = SettingsManager.Instance.WorkingDirectory

                If String.IsNullOrWhiteSpace(workingDir) Then
                    item.Status = "Error: Working directory not set."
                    Continue For
                End If

                If Not Directory.Exists(workingDir) Then
                    Directory.CreateDirectory(workingDir)
                End If

                session.ChangeDirectory(workingDir)

                Dim desc = (New CCpfcModelDescriptor()) _
                    .Create(EpfcModelType.EpfcMDL_DRAWING, modelName, Nothing)

                Dim model As IpfcModel = session.RetrieveModel(desc)

                If model Is Nothing Then
                    item.Status = "Failed to retrieve"
                    Continue For
                End If

                ' Exact same naming logic as ViewModel
                Dim baseName As String =
                    If(String.IsNullOrWhiteSpace(item.CustomPdfName),
                       modelName,
                       Path.GetFileNameWithoutExtension(item.CustomPdfName))
                'Converting to uppercase as client requested 
                baseName = baseName.ToUpperInvariant()

                Dim pdfPath As String =
                    Path.Combine(outputFolder, baseName & ".pdf")

                ' ⚠ Do NOT blindly delete.
                ' File already validated by ViewModel.
                If File.Exists(pdfPath) Then
                    File.Delete(pdfPath)
                End If

                ' ================= PDF OPTIONS =================
                Dim pdfInstr As IpfcPDFExportInstructions =
                    (New CCpfcPDFExportInstructions()).Create()

                pdfInstr.FilePath = pdfPath

                Dim pdfOptions As IpfcPDFOptions = New CpfcPDFOptions()

                ' Disable viewer
                Dim optViewer As IpfcPDFOption =
                    (New CCpfcPDFOption()).Create()
                ' Export as Grayscale
                Dim optColorDepth As IpfcPDFOption =
    (New CCpfcPDFOption()).Create()

                optColorDepth.OptionType =
    EpfcPDFOptionType.EpfcPDFOPT_COLOR_DEPTH

                optColorDepth.OptionValue =
    (New CMpfcArgument()).CreateIntArgValue(
        EpfcPDFColorDepth.EpfcPDF_CD_GRAY)

                pdfOptions.Append(optColorDepth)
                optViewer.OptionType =
                    EpfcPDFOptionType.EpfcPDFOPT_LAUNCH_VIEWER
                optViewer.OptionValue =
                    (New CMpfcArgument()).CreateBoolArgValue(False)
                pdfOptions.Append(optViewer)

                ' Export all sheets
                Dim optSheets As IpfcPDFOption =
    (New CCpfcPDFOption()).Create()

                optSheets.OptionType =
    EpfcPDFOptionType.EpfcPDFOPT_SHEETS

                optSheets.OptionValue =
    (New CMpfcArgument()).CreateIntArgValue(1)

                pdfOptions.Append(optSheets)

                ' Single PDF mode
                'Dim optMode As IpfcPDFOption =
                '    (New CCpfcPDFOption()).Create()
                'optMode.OptionType =
                '    EpfcPDFOptionType.EpfcPDFOPT_EXPORT_MODE
                'optMode.OptionValue =
                '    (New CMpfcArgument()).CreateIntArgValue(0)
                'pdfOptions.Append(optMode)

                pdfInstr.Options = pdfOptions

                ' ================= EXPORT =================
                model.Display()
                model.Export(pdfPath, pdfInstr)

                ' ================= WATERMARK =================
                If SettingsManager.Instance.EnableWatermark Then

                    Dim tempPdf As String =
                        Path.Combine(
                            Path.GetDirectoryName(pdfPath),
                            Path.GetFileNameWithoutExtension(pdfPath) & "_wm.pdf"
                        )

                    WatermarkHelper.AddWatermarkToPdf(
                        pdfPath,
                        tempPdf,
                        SettingsManager.Instance.WatermarkText,
                        CSng(SettingsManager.Instance.WatermarkOpacity),
                        CSng(SettingsManager.Instance.WatermarkFontSize),
                        0,
                        SettingsManager.Instance.WatermarkColor,
                        SettingsManager.Instance.WatermarkPosition
                    )

                    File.Delete(pdfPath)
                    File.Move(tempPdf, pdfPath)
                End If

                ' Cleanup
                Dim activeWindow As IpfcWindow = session.CurrentWindow
                If activeWindow IsNot Nothing Then activeWindow.Close()

                session.EraseUndisplayedModels()

                item.Status = "Success"
                exportedPdfPaths.Add(pdfPath)

            Catch ex As Exception
                item.Status = "Error: " & ex.Message
            End Try

        Next

        '================= MERGE =================
        If isMergeEnabled AndAlso exportedPdfPaths.Any() Then
            Try
                Dim mergedPdfPath =
                Path.Combine(outputFolder, "MERGED DRAWINGS.pdf")
                'Path.Combine(outputFolder, "Merged Drawings.pdf")

                Using fs As New FileStream(mergedPdfPath,
                                           FileMode.Create,
                                           FileAccess.Write,
                                           FileShare.None)

                    Using document As New iTextSharp.text.Document()
                        Using copy As New PdfSmartCopy(document, fs)

                            document.Open()

                            For Each pdfFile In exportedPdfPaths
                                If Not File.Exists(pdfFile) Then Continue For

                                Using reader As New PdfReader(pdfFile)
                                    For pageNum As Integer = 1 To reader.NumberOfPages
                                        copy.AddPage(copy.GetImportedPage(reader, pageNum))
                                    Next
                                End Using
                            Next

                            document.Close()
                        End Using
                    End Using
                End Using

                ' Delete individual PDFs after merge
                For Each filePath In exportedPdfPaths
                    If File.Exists(filePath) Then File.Delete(filePath)
                Next

            Catch mergeEx As Exception
                MessageBox.Show("PDF merge failed:" & vbCrLf &
                                mergeEx.Message,
                                "Merge Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error)
            End Try
        End If

    End Sub
End Class
