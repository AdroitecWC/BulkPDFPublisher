Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Security.AccessControl
Imports PdfSharp.Pdf
Imports PdfSharp.Pdf.IO
Imports pfcls
Imports PdfSharp.Drawing
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
                    ErrorLogger.LogMessage("Creo", "Working directory is not set.")
                    MessageBox.Show("Working directory is not set. Please set it in Settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                    Exit Sub
                End If

                If Not System.IO.Directory.Exists(workingDir) Then
                    Try
                        System.IO.Directory.CreateDirectory(workingDir)
                    Catch ex As Exception
                        ErrorLogger.LogException($"Create working directory: {workingDir}", ex)
                        MessageBox.Show("Unable to create working directory: " & ex.Message & vbCrLf & vbCrLf &
                                        "Details written to:" & vbCrLf & ErrorLogger.LogFilePath,
                                        "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                        Exit Sub
                    End Try
                End If


                ' Set Creo's working directory
                Try
                    session.ChangeDirectory(workingDir)
                Catch ex As Exception
                    ErrorLogger.LogException($"Change Creo working directory: {workingDir}", ex)
                    MessageBox.Show("Failed to change Creo directory: " & ErrorLogger.FormatBrief(ex) & vbCrLf & vbCrLf &
                                    "Details written to:" & vbCrLf & ErrorLogger.LogFilePath,
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error)
                    Exit Sub
                End Try

                Dim model As IpfcModel = RetrieveDrawing(session, item.FullPath, workingDir)

                If model Is Nothing Then
                    item.Status = "Failed to retrieve"
                    ErrorLogger.LogMessage("Creo", $"RetrieveModel returned Nothing for '{modelName}' (file: {item.FullPath}).")
                    Continue For
                End If

                Dim pdfInstr As IpfcPDFExportInstructions = (New CCpfcPDFExportInstructions()).Create()
                Dim customName = If(String.IsNullOrWhiteSpace(item.CustomPdfName), modelName, item.CustomPdfName).ToUpperInvariant()
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

                ' Force monochrome color depth
                Dim pdfOpt2 As New CCpfcPDFOption
                Dim opt2 As IpfcPDFOption = pdfOpt2.Create()
                opt2.OptionType = EpfcPDFOptionType.EpfcPDFOPT_COLOR_DEPTH
                Dim arg2 As New CMpfcArgument
                opt2.OptionValue = arg2.CreateIntArgValue(CInt(EpfcPDFColorDepth.EpfcPDF_CD_MONO))
                pdfOptions.Append(opt2)

                pdfInstr.Options = pdfOptions

                ' Remove an old copy first, so the wait below can't mistake it for the new PDF.
                If File.Exists(pdfPath) Then File.Delete(pdfPath)

                model.Display()
                model.Export(pdfPath, pdfInstr)

                ' Don't move on to the next drawing until this PDF is fully written.
                If Not WaitForPdfReady(pdfPath, TimeSpan.FromMinutes(5)) Then
                    item.Status = "Error: PDF was not created in time"
                    ErrorLogger.LogMessage("Creo", $"PDF not ready after export: {pdfPath} (file: {item.FullPath}).")
                    Continue For
                End If

                item.Status = "Success"
                exportedPdfPaths.Add(pdfPath)

            Catch ex As Exception
                ErrorLogger.LogException($"Export PDF: {item.FullPath}", ex)
                item.Status = "Error: " & ErrorLogger.FormatBrief(ex)
            Finally
                ' Always clean the session, even after a failure, so this drawing's
                ' models can't clash with same-named models of the next drawing.
                CleanUpSession(session)
            End Try
        Next
        ' Only merge if user requested
        If isMergeEnabled Then
            Try
                Dim mergedPdfPath = Path.Combine(outputFolder, "MergedDrawings.pdf")
                Using outputDocument As New PdfDocument()
                    For Each pdfFile In exportedPdfPaths
                        If File.Exists(pdfFile) Then
                            Using inputDocument = PdfReader.Open(pdfFile, PdfDocumentOpenMode.Import)
                                For i = 0 To inputDocument.PageCount - 1
                                    outputDocument.AddPage(inputDocument.Pages(i))
                                Next
                            End Using
                        End If
                    Next
                    outputDocument.Save(mergedPdfPath)

                    ' Optionally delete individual PDFs after merging
                    ' Delete individual PDFs after merging
                    For Each filePath In exportedPdfPaths
                        If File.Exists(filePath) Then
                            File.Delete(filePath)
                        End If
                    Next

                End Using
            Catch mergeEx As Exception
                ErrorLogger.LogException($"Merge PDFs into MergedDrawings.pdf ({exportedPdfPaths.Count} source files)", mergeEx)
                MessageBox.Show("PDF merge failed: " & ErrorLogger.FormatBrief(mergeEx) & vbCrLf & vbCrLf &
                                "Details written to:" & vbCrLf & ErrorLogger.LogFilePath,
                                "Merge Failed", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End If


    End Sub

    ' Creo drawing files on disk can carry a version suffix (name.drw.3); the model
    ' name Creo expects is just "name".
    Private Shared Function GetDrawingModelName(filePath As String) As String
        Dim fileName = Path.GetFileName(filePath)
        Return Regex.Replace(fileName, "\.drw(\.\d+)?$", "", RegexOptions.IgnoreCase)
    End Function

    ' Retrieve a drawing from its own folder first (so it and its referenced
    ' asm/prt/frm files are found next to it), then fall back to the configured
    ' working directory. Each location is tried twice, since Creo can reject the
    ' first calls while it is still finishing startup.
    Private Function RetrieveDrawing(session As IpfcBaseSession, filePath As String, workingDir As String) As IpfcModel
        Dim modelName = GetDrawingModelName(filePath)
        Dim folders As New List(Of String)
        Dim fileFolder = Path.GetDirectoryName(filePath)
        If Not String.IsNullOrWhiteSpace(fileFolder) Then folders.Add(fileFolder)
        If Not folders.Any(Function(f) String.Equals(Path.GetFullPath(f).TrimEnd("\"c), Path.GetFullPath(workingDir).TrimEnd("\"c), StringComparison.OrdinalIgnoreCase)) Then
            folders.Add(workingDir)
        End If

        Dim lastError As Exception = Nothing
        For Each folder In folders
            For attempt = 1 To 2
                Try
                    session.ChangeDirectory(folder)
                    Dim desc = (New CCpfcModelDescriptor()).Create(EpfcModelType.EpfcMDL_DRAWING, modelName, Nothing)
                    Return session.RetrieveModel(desc)
                Catch ex As Exception
                    lastError = ex
                    ErrorLogger.LogException($"RetrieveModel '{modelName}' from '{folder}' (attempt {attempt})", ex)
                    CleanUpSession(session)
                    If attempt = 1 Then System.Threading.Thread.Sleep(2000)
                End Try
            Next
        Next
        Throw lastError
    End Function

    ' Close the displayed window and erase everything not displayed. Failures are
    ' logged but never thrown, so they can't flip a successful export to "Error".
    Private Sub CleanUpSession(session As IpfcBaseSession)
        Try
            Dim activeWindow As IpfcWindow = session.CurrentWindow
            If activeWindow IsNot Nothing Then activeWindow.Close()
        Catch ex As Exception
            ErrorLogger.LogException("Close Creo window", ex)
        End Try
        Try
            session.EraseUndisplayedModels()
        Catch ex As Exception
            ErrorLogger.LogException("Erase undisplayed models", ex)
        End Try
    End Sub

    ' Wait until the exported PDF exists, has stopped growing, and is no longer
    ' locked by Creo. Returns False if that doesn't happen within the timeout.
    Private Shared Function WaitForPdfReady(pdfPath As String, timeout As TimeSpan) As Boolean
        Dim deadline = DateTime.Now.Add(timeout)
        Dim lastSize As Long = -1
        Do While DateTime.Now < deadline
            If File.Exists(pdfPath) Then
                Dim size = New FileInfo(pdfPath).Length
                If size > 0 AndAlso size = lastSize Then
                    Try
                        Using File.Open(pdfPath, FileMode.Open, FileAccess.Read, FileShare.None)
                        End Using
                        Return True
                    Catch ex As IOException
                        ' Still being written; keep waiting.
                    End Try
                End If
                lastSize = size
            End If
            System.Threading.Thread.Sleep(500)
        Loop
        Return False
    End Function
End Class
