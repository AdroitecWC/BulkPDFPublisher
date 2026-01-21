Imports System.IO
Imports iTextSharp.text
Imports iTextSharp.text.pdf
Imports pfcls

Public Class ProECommonFunctionalites

    ' Set Creo config option
    Public Sub setConfigOption(configName As String, configValue As String)
        Dim creoSession As IpfcBaseSession = CreoSessionManager.Instance.Session
        creoSession.SetConfigOption(configName, configValue)
    End Sub
    ' Export all selected drawings with custom PDF names

    Public Sub ExportAllDrawingsAsPDF(fileItems As List(Of FileItemModel),
                                          outputFolder As String, isMergeEnabled As Boolean)

        Dim session = CreoSessionManager.Instance.Session
        Dim exportedPdfPaths As New List(Of String)()

        For Each item In fileItems.Where(Function(f) f.IsSelected)
            Try
                Dim modelName = Path.GetFileNameWithoutExtension(item.FullPath)

                Dim workingDir = SettingsManager.Instance.WorkingDirectory
                ' Validate directory
                If String.IsNullOrWhiteSpace(workingDir) Then
                    MessageBox.Show("Working directory is not set. Please set it in Settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                    Exit Sub
                End If

                If Not System.IO.Directory.Exists(workingDir) Then
                    Try
                        System.IO.Directory.CreateDirectory(workingDir)
                    Catch ex As Exception
                        MessageBox.Show("Unable to create working directory: " & ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                        Exit Sub
                    End Try
                End If


                session.ChangeDirectory(workingDir)

                ' Set Creo's working directory
                Try
                    session.ChangeDirectory(workingDir)
                Catch ex As Exception
                    MessageBox.Show("Failed to change Creo directory: " & ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                    Exit Sub
                End Try
                Dim desc = (New CCpfcModelDescriptor()).Create(EpfcModelType.EpfcMDL_DRAWING, modelName, Nothing)
                Dim model As IpfcModel = session.RetrieveModel(desc)

                If model Is Nothing Then
                    item.Status = "Failed to retrieve"
                    Continue For
                End If

                Dim pdfInstr As IpfcPDFExportInstructions = (New CCpfcPDFExportInstructions()).Create()
                Dim customName = If(String.IsNullOrWhiteSpace(item.CustomPdfName), modelName, item.CustomPdfName)
                Dim pdfPath = Path.Combine(outputFolder, customName & ".pdf")
                pdfInstr.FilePath = pdfPath

                ' Disable viewer launch
                Dim pdfOptions As IpfcPDFOptions = New CpfcPDFOptions()
                Dim pdfOpt1 As New CCpfcPDFOption
                Dim opt1 As IpfcPDFOption = pdfOpt1.Create()
                opt1.OptionType = EpfcPDFOptionType.EpfcPDFOPT_LAUNCH_VIEWER
                Dim arg1 As New CMpfcArgument
                opt1.OptionValue = arg1.CreateBoolArgValue(False)
                pdfOptions.Append(opt1)


                pdfInstr.Options = pdfOptions
                pdfInstr.Options = pdfOptions

                model.Display()
                model.Export(pdfPath, pdfInstr)

                Dim activeWindow As IpfcWindow = session.CurrentWindow
                If activeWindow IsNot Nothing Then activeWindow.Close()

                session.EraseUndisplayedModels()

                item.Status = "Success"
                exportedPdfPaths.Add(pdfPath)

            Catch ex As Exception
                item.Status = "Error: " & ex.Message
            End Try
        Next
        ' Only merge if user requested
        If isMergeEnabled AndAlso exportedPdfPaths.Any() Then
            Try
                Dim mergedPdfPath = Path.Combine(outputFolder, "MergedDrawings.pdf")

                Using fs As New FileStream(mergedPdfPath, FileMode.Create, FileAccess.Write, FileShare.None)
                    Using document As New Document()
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

                ' Delete individual PDFs ONLY after successful merge
                For Each filePath In exportedPdfPaths
                    If File.Exists(filePath) Then
                        File.Delete(filePath)
                    End If
                Next

            Catch mergeEx As Exception
                MessageBox.Show(
            "PDF merge failed:" & vbCrLf & mergeEx.Message,
            "Merge Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error)
            End Try
        End If


    End Sub
End Class