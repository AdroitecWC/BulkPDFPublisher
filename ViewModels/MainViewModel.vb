Imports System.Collections.ObjectModel
Imports System.Collections.Specialized.BitVector32
Imports System.ComponentModel
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Runtime.CompilerServices
Imports System.Text.RegularExpressions
Imports System.Windows ' For MessageBox and Window
Imports System.Windows.Data
Imports System.Windows.Forms.VisualStyles.VisualStyleElement
Imports System.Windows.Input
Imports System.Windows.Media
Imports Microsoft.Win32
Imports OfficeOpenXml
Imports OfficeOpenXml.Style
Imports Ookii.Dialogs.Wpf
Imports pfcls

Namespace CreoPublisherApp.ViewModels

    Public Class MainViewModel
        Implements INotifyPropertyChanged

        Private _inputPath As String = ""
        Private _outputPath As String = ""
        Private _files As ObservableCollection(Of FileItemModel) = New ObservableCollection(Of FileItemModel)()
        Private _filteredFilesView As ICollectionView

        Private _log As String = ""
        Private _filesCountText As String = "No files selected."

        Private _createdDateStart As Date?
        Private _createdDateEnd As Date?
        Private _modifiedDateStart As Date?
        Private _modifiedDateEnd As Date?
        Private _isAllSelected As Boolean = False
        Private _selectDeselectButtonText As String = "Select All"
        Private _searchText As String = String.Empty
        Private _isMergeEnabled As Boolean = False

        ' New properties for UI display
        Private _totalFilesCount As Integer = 0
        Private _selectedFilesCount As Integer = 0
        Private _filesToRenameCount As Integer = 0
        Private _publishButtonText As String = "🚀 Export 0 Files"
        Private _lastExportedCsvPath As String


        ' Creo automation classes
        Private _openProe As New OpenProeObjectClass()
        Private _proeCommonFuncs As New ProECommonFunctionalites()

        ' Command to open Settings window
        Public Property OpenSettingsWindowCommand As ICommand
        Public Property OpenLicenseSettingsWindowCommand As ICommand

        Public Sub New()
            _filteredFilesView = CollectionViewSource.GetDefaultView(_files)
            _filteredFilesView.Filter = AddressOf FilterFiles

            ' Commands
            BrowseFolderCommand = New RelayCommand(AddressOf BrowseFolder)
            BrowseFilesCommand = New RelayCommand(AddressOf BrowseFiles)
            BrowseOutputFolderCommand = New RelayCommand(AddressOf BrowseOutputFolder)
            SelectAllCommand = New RelayCommand(AddressOf SelectAllFiles)
            DeselectAllCommand = New RelayCommand(AddressOf DeselectAllFiles)
            InvertSelectionCommand = New RelayCommand(AddressOf InvertSelectionFiles)
            PublishCommand = New RelayCommand(AddressOf PublishFiles)
            ToggleSelectCommand = New RelayCommand(AddressOf ToggleSelectDeselect)
            ClearFiltersCommand = New RelayCommand(AddressOf ClearFilters)

            OpenSettingsWindowCommand = New RelayCommand(AddressOf OpenSettingsWindow)
            OpenLicenseSettingsWindowCommand = New RelayCommand(AddressOf OpenLicenseSettingsWindow)
            ExportReportCommand = New RelayCommand(AddressOf ExportReportCommandExecute)
            OpenReportCommand = New RelayCommand(AddressOf OpenReportCommandExecute, AddressOf CanOpenReport)
        End Sub



        ' Properties
        Public Property InputPath As String
            Get
                Return _inputPath
            End Get
            Set(value As String)
                If _inputPath <> value Then
                    Dim cleanedValue = value?.Trim().Trim(""""c)
                    _inputPath = cleanedValue
                    OnPropertyChanged()
                    OnPropertyChanged(NameOf(HasInputFolder))
                    OnPropertyChanged(NameOf(NoInputFolderText))
                    If String.IsNullOrEmpty(_inputPath) Then Return
                    If _inputPath.Equals("Multiple files selected", StringComparison.OrdinalIgnoreCase) Then Return

                    ' Handle single file selection
                    If File.Exists(_inputPath) AndAlso Path.GetExtension(_inputPath).Equals(".drw", StringComparison.OrdinalIgnoreCase) Then
                        ' Update working directory to the directory containing the file
                        Dim fileDirectory = Path.GetDirectoryName(_inputPath)
                        UpdateWorkingDirectory(fileDirectory)
                        Return
                    End If

                    ' Handle folder selection
                    If Directory.Exists(_inputPath) Then
                        ' Update working directory to the selected folder
                        UpdateWorkingDirectory(_inputPath)

                        LoadFilesFromFolder(_inputPath)
                        If IsCliMode Then
                            For Each f In _files
                                f.IsSelected = True
                            Next
                            UpdateFilesCountText()
                            UpdateCounts()
                        End If
                    Else
                        Log &= $"Warning: Input folder or file '{_inputPath}' does not exist or is invalid.{Environment.NewLine}"
                        _files.Clear()
                        UpdateFilesCountText()
                        UpdateCounts()
                        RefreshFilter()
                    End If
                End If
                CompleteStep(1)
            End Set
        End Property


        Public Property OutputPath As String
            Get
                Return _outputPath
            End Get
            Set(value As String)
                If _outputPath <> value Then
                    Dim cleanedValue = value?.Trim().Trim(""""c)
                    _outputPath = cleanedValue
                    OnPropertyChanged()
                    OnPropertyChanged(NameOf(HasOutputFolder))
                    OnPropertyChanged(NameOf(NoOutputFolderText))
                End If
                CompleteStep(2)
            End Set

        End Property

        Public Property SearchText As String
            Get
                Return _searchText
            End Get
            Set(value As String)
                If _searchText <> value Then
                    _searchText = value
                    OnPropertyChanged()
                    RefreshFilter()
                End If
            End Set
        End Property

        Public ReadOnly Property Files As ObservableCollection(Of FileItemModel)
            Get
                Return _files
            End Get
        End Property

        Public ReadOnly Property FilteredFiles As ICollectionView
            Get
                Return _filteredFilesView
            End Get
        End Property

        Public Property CreatedDateStart As Date?
            Get
                Return _createdDateStart
            End Get
            Set(value As Date?)
                _createdDateStart = value
                OnPropertyChanged()
                RefreshFilter()
            End Set
        End Property

        Public Property CreatedDateEnd As Date?
            Get
                Return _createdDateEnd
            End Get
            Set(value As Date?)
                _createdDateEnd = value
                OnPropertyChanged()
                RefreshFilter()
            End Set
        End Property

        Public Property ModifiedDateStart As Date?
            Get
                Return _modifiedDateStart
            End Get
            Set(value As Date?)
                _modifiedDateStart = value
                OnPropertyChanged()
                RefreshFilter()
            End Set
        End Property

        Public Property ModifiedDateEnd As Date?
            Get
                Return _modifiedDateEnd
            End Get
            Set(value As Date?)
                _modifiedDateEnd = value
                OnPropertyChanged()
                RefreshFilter()
            End Set
        End Property

        Public Property Log As String
            Get
                Return _log
            End Get
            Set(value As String)
                If _log <> value Then
                    _log = value
                    OnPropertyChanged()
                    CompleteStep(4)
                End If
            End Set
        End Property

        Public ReadOnly Property FilesCountText As String
            Get
                Return _filesCountText
            End Get
        End Property
        Public ReadOnly Property IsCliMode As Boolean
            Get
                Return Application.Current Is Nothing OrElse Application.Current.MainWindow Is Nothing
            End Get
        End Property

        Public Property IsMergeEnabled As Boolean
            Get
                Return _isMergeEnabled
            End Get
            Set(value As Boolean)
                _isMergeEnabled = value
                OnPropertyChanged(NameOf(IsMergeEnabled))
                UpdatePublishButtonText()
                CompleteStep(3) ' Mark step 3 as completed
            End Set
        End Property

        ' New properties for the sidebar display
        Public ReadOnly Property TotalFilesCount As String
            Get
                Return _totalFilesCount.ToString()
            End Get
        End Property

        Public ReadOnly Property SelectedFilesCount As String
            Get
                Return _selectedFilesCount.ToString()
            End Get
        End Property

        Public ReadOnly Property FilesToRenameCount As String
            Get
                Return _filesToRenameCount.ToString()
            End Get
        End Property



        Public ReadOnly Property FilesDisplayText As String
            Get
                Return $"{_totalFilesCount} files"
            End Get
        End Property

        ' Property to check if output folder is selected
        Public ReadOnly Property HasOutputFolder As Boolean
            Get
                Return Not String.IsNullOrEmpty(_outputPath) AndAlso Directory.Exists(_outputPath)
            End Get
        End Property
        Public ReadOnly Property HasInputFolder As Boolean
            Get
                Return Not String.IsNullOrEmpty(_inputPath) AndAlso
               (_inputPath.Equals("Multiple files selected", StringComparison.OrdinalIgnoreCase) = False) AndAlso
               (File.Exists(_inputPath) OrElse Directory.Exists(_inputPath))
            End Get
        End Property
        Public ReadOnly Property NoInputFolderText As String
            Get
                Return If(HasInputFolder, "", "No input folder selected")
            End Get
        End Property

        Public ReadOnly Property NoOutputFolderText As String
            Get
                Return If(HasOutputFolder, "", "No output folder selected")
            End Get
        End Property
        Public Property SelectDeselectButtonText As String
            Get
                Return _selectDeselectButtonText
            End Get
            Set(value As String)
                _selectDeselectButtonText = value
                OnPropertyChanged(NameOf(SelectDeselectButtonText))
            End Set
        End Property

        Private _isStep1Completed As Boolean
        Public Property IsStep1Completed As Boolean
            Get
                Return _isStep1Completed
            End Get
            Set(value As Boolean)
                If _isStep1Completed <> value Then
                    _isStep1Completed = value
                    RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(IsStep1Completed)))
                    RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(CanSelectOutputFolder)))
                End If
            End Set
        End Property

        Private _isStep2Completed As Boolean
        Public Property IsStep2Completed As Boolean
            Get
                Return _isStep2Completed
            End Get
            Set(value As Boolean)
                If _isStep2Completed <> value Then
                    _isStep2Completed = value
                    RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(IsStep2Completed)))
                    RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(CanReviewPublish)))
                End If
            End Set
        End Property

        Private _isStep3Completed As Boolean
        Public Property IsStep3Completed As Boolean
            Get
                Return _isStep3Completed
            End Get
            Set(value As Boolean)
                If _isStep3Completed <> value Then
                    _isStep3Completed = value
                    RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(IsStep3Completed)))
                End If
            End Set
        End Property

        Private _isStep4Completed As Boolean
        Public Property IsStep4Completed As Boolean
            Get
                Return _isStep4Completed
            End Get
            Set(value As Boolean)
                If _isStep4Completed <> value Then
                    _isStep4Completed = value
                    RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(IsStep4Completed)))
                End If
            End Set
        End Property

        Public ReadOnly Property CanSelectOutputFolder As Boolean
            Get
                Return IsStep1Completed
            End Get
            ' no setter, auto dependent on Step1 completion
        End Property

        Public ReadOnly Property CanReviewPublish As Boolean
            Get
                Return IsStep2Completed
            End Get
        End Property

        Private _reportFilePath As String
        Public Property ReportFilePath As String
            Get
                Return _reportFilePath
            End Get
            Set(value As String)
                _reportFilePath = value
                OnPropertyChanged(NameOf(ReportFilePath))
            End Set
        End Property
        Private _isPublishing As Boolean = False

        Public Property IsPublishing As Boolean
            Get
                Return _isPublishing
            End Get
            Set(value As Boolean)
                If _isPublishing <> value Then
                    _isPublishing = value
                    OnPropertyChanged()
                    OnPropertyChanged(NameOf(PublishButtonText))
                    ' Update CanExecute for the button
                    CType(PublishCommand, RelayCommand).RaiseCanExecuteChanged()
                End If
            End Set
        End Property

        Public ReadOnly Property PublishButtonText As String
            Get
                If IsPublishing Then
                    Return "Publishing..."
                Else
                    Return $"🚀 {If(IsMergeEnabled, "Merge", "Export")} {_selectedFilesCount} Files"
                End If
            End Get
        End Property

        ' Commands
        Public Property BrowseFolderCommand As ICommand
        Public Property BrowseFilesCommand As ICommand
        Public Property BrowseOutputFolderCommand As ICommand
        Public Property SelectAllCommand As ICommand
        Public Property DeselectAllCommand As ICommand
        Public Property InvertSelectionCommand As ICommand
        Public Property PublishCommand As ICommand
        Public Property ToggleSelectCommand As ICommand
        Public Property ClearFiltersCommand As ICommand

        Public Property OpenReportCommand As ICommand
        Public Property ExportReportCommand As ICommand

        ' Filtering logic
        Private Function FilterFiles(obj As Object) As Boolean
            Dim file = TryCast(obj, FileItemModel)
            If file Is Nothing Then Return False

            Dim matchesSearch As Boolean = True

            If Not String.IsNullOrWhiteSpace(SearchText) Then
                ' Split by comma, trim, and convert to lowercase
                Dim searchTerms = SearchText.Split(","c).
            Select(Function(s) s.Trim().ToLower()).
            Where(Function(s) Not String.IsNullOrWhiteSpace(s)).
            ToList()

                ' Match if any term is found in the filename
                matchesSearch = searchTerms.Any(Function(term) file.FileName.ToLower().Contains(term))
            End If
            Dim matchesCreatedStart As Boolean = Not CreatedDateStart.HasValue OrElse file.CreatedDateRaw >= CreatedDateStart.Value
            Dim matchesCreatedEnd As Boolean = Not CreatedDateEnd.HasValue OrElse file.CreatedDateRaw <= CreatedDateEnd.Value
            Dim matchesModifiedStart As Boolean = Not ModifiedDateStart.HasValue OrElse file.ModifiedDateRaw >= ModifiedDateStart.Value
            Dim matchesModifiedEnd As Boolean = Not ModifiedDateEnd.HasValue OrElse file.ModifiedDateRaw <= ModifiedDateEnd.Value

            Return matchesSearch AndAlso matchesCreatedStart AndAlso matchesCreatedEnd AndAlso matchesModifiedStart AndAlso matchesModifiedEnd
        End Function

        Private Sub RefreshFilter()
            _filteredFilesView.Refresh()
            UpdateCounts()
        End Sub

        ' File loading and updating
        Private Sub BrowseFolder()
            Dim dialog As New VistaFolderBrowserDialog()
            If dialog.ShowDialog() = True Then
                InputPath = dialog.SelectedPath
                LoadFilesFromFolder(InputPath)
            End If
        End Sub

        Private Sub BrowseFiles()
            Dim dlg As New OpenFileDialog()
            dlg.Multiselect = True
            dlg.Filter = "Creo Drawing Files (*.drw)|*.drw"

            If dlg.ShowDialog() = True Then
                _files.Clear()
                For Each filePath In dlg.FileNames
                    Dim fileModel As New FileItemModel(filePath)
                    fileModel.IsSelected = True ' ✅ Select immediately
                    AddHandler fileModel.PropertyChanged, AddressOf FileItem_PropertyChanged
                    _files.Add(fileModel)
                Next
                ' Don't set InputPath, it causes folder validation warnings
                UpdateFilesCountText()
                UpdateCounts()
                RefreshFilter()
            End If
        End Sub

        Private Sub BrowseOutputFolder()
            Dim dialog As New VistaFolderBrowserDialog()
            If dialog.ShowDialog() = True Then
                OutputPath = dialog.SelectedPath
                CompleteStep(2) ' Mark step 2 as completed  
            End If
        End Sub

        Private Sub LoadFilesFromFolder(folderPath As String)
            Try
                _files.Clear()

                ' Get all .drw files (including .drw.1, .drw.2, etc.)
                Dim filesInFolder = Directory.GetFiles(folderPath).
            Where(Function(f) Regex.IsMatch(System.IO.Path.GetFileName(f), "\.drw(\.\d+)?$", RegexOptions.IgnoreCase)).
            ToList()

                ' Per drawing name keep only the latest iteration (highest version number)
                Dim uniqueFiles = KeepLatestIterations(filesInFolder)

                ' Load remaining files
                For Each filePath In uniqueFiles
                    Dim fileModel As New FileItemModel(filePath)
                    AddHandler fileModel.PropertyChanged, AddressOf FileItem_PropertyChanged
                    _files.Add(fileModel)
                Next

                UpdateFilesCountText()
                UpdateCounts()
                RefreshFilter()

            Catch ex As Exception
                ErrorLogger.LogException($"Load files from folder: {_inputPath}", ex)
                Log &= $"Error loading files from folder: {ErrorLogger.FormatBrief(ex)} (details: {ErrorLogger.LogFilePath}){Environment.NewLine}"
            End Try
        End Sub

        ' Groups files by drawing base name and returns only the highest iteration of each.
        ' e.g. bracket.drw.1, bracket.drw.2, bracket.drw.3 → bracket.drw.3
        ' Files without a version suffix (bracket.drw) are treated as iteration 0
        ' and kept only when no numbered version exists for that drawing.
        Private Function KeepLatestIterations(filePaths As List(Of String)) As List(Of String)
            Return filePaths.
                GroupBy(Function(f)
                            Regex.Replace(Path.GetFileName(f), "\.drw(\.\d+)?$", "", RegexOptions.IgnoreCase).ToLowerInvariant()
                        End Function).
                Select(Function(g)
                           Return g.OrderByDescending(Function(f)
                                                          Dim m = Regex.Match(Path.GetFileName(f), "\.drw\.(\d+)$", RegexOptions.IgnoreCase)
                                                          Return If(m.Success, Integer.Parse(m.Groups(1).Value), 0)
                                                      End Function).First()
                       End Function).
                ToList()
        End Function

        ' Validates whether a file exists and can be opened for reading.
        ' Logs the outcome and returns True if the file is accessible, False otherwise.
        Private Function ValidateFileAccess(filePath As String) As Boolean
            Try
                If Not File.Exists(filePath) Then
                    Log &= $"FILE CHECK: File does not exist: {filePath}{Environment.NewLine}"
                    Return False
                End If

                Log &= $"FILE CHECK: File exists - {filePath}{Environment.NewLine}"

                Using fs As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                    Log &= $"FILE ACCESS: Read access successful ({Path.GetFileName(filePath)}){Environment.NewLine}"
                    Log &= $"FILE SIZE: {fs.Length} bytes{Environment.NewLine}"
                End Using

                ' ---------- Blocking / locking checks ----------
                Dim attrs = File.GetAttributes(filePath)

                If (attrs And FileAttributes.ReadOnly) = FileAttributes.ReadOnly Then
                    Log &= $"FILE ATTRIBUTE: Read-only flag is set - {Path.GetFileName(filePath)}{Environment.NewLine}"
                End If

                ' Exclusive-lock probe: if another process (Creo, Explorer preview, etc.) holds the file, this throws.
                Try
                    Using fsLock As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None)
                        ' Acquired exclusive handle — file is not locked.
                    End Using
                Catch lockEx As IOException
                    Log &= $"FILE LOCK: File appears to be in use by another process - {Path.GetFileName(filePath)} ({lockEx.Message}){Environment.NewLine}"
                End Try

                ' Mark-of-the-Web (Zone.Identifier alternate data stream) - Creo may block files downloaded from the internet.
                If File.Exists(filePath & ":Zone.Identifier") Then
                    Log &= $"FILE ZONE: Mark-of-the-Web detected (downloaded from internet) - {Path.GetFileName(filePath)}{Environment.NewLine}"
                End If

                ' ---------- Path / location hazards ----------
                If filePath.Length > 248 Then
                    Log &= $"PATH LENGTH: Path is {filePath.Length} chars (>248); Creo may fail to open long paths - {filePath}{Environment.NewLine}"
                End If

                If filePath.Any(Function(c) AscW(c) > 127) Then
                    Log &= $"PATH ENCODING: Path contains non-ASCII characters - {filePath}{Environment.NewLine}"
                End If

                Try
                    Dim root = Path.GetPathRoot(filePath)
                    If filePath.StartsWith("\\") Then
                        Log &= $"PATH LOCATION: UNC network path - {root}{Environment.NewLine}"
                    ElseIf Not String.IsNullOrEmpty(root) Then
                        Dim di As New DriveInfo(root)
                        Select Case di.DriveType
                            Case DriveType.Network
                                Log &= $"PATH LOCATION: Network drive ({root}){Environment.NewLine}"
                            Case DriveType.Removable
                                Log &= $"PATH LOCATION: Removable drive ({root}){Environment.NewLine}"
                            Case DriveType.CDRom
                                Log &= $"PATH LOCATION: CD/DVD drive ({root}) - read-only media{Environment.NewLine}"
                        End Select
                    End If
                Catch driveEx As Exception
                    Log &= $"PATH LOCATION: Could not determine drive type - {driveEx.Message}{Environment.NewLine}"
                End Try

                Return True

            Catch ex As UnauthorizedAccessException
                ErrorLogger.LogException($"Validate file access: {filePath}", ex)
                Log &= $"FILE ACCESS DENIED: {ex.Message}{Environment.NewLine}"
                Return False
            Catch ex As IOException
                ErrorLogger.LogException($"Validate file access: {filePath}", ex)
                Log &= $"FILE IO ERROR: {ex.Message}{Environment.NewLine}"
                Return False
            Catch ex As Exception
                ErrorLogger.LogException($"Validate file access: {filePath}", ex)
                Log &= $"FILE CHECK ERROR: {ex.Message}{Environment.NewLine}"
                Return False
            End Try
        End Function


        Private Sub UpdateFilesCountText()
            Dim selectedCount = _files.Where(Function(f) f.IsSelected).Count()
            _filesCountText = $"{selectedCount} file(s) selected"
            OnPropertyChanged(NameOf(FilesCountText))
            UpdateCounts()
        End Sub

        Private Sub UpdateCounts()
            _totalFilesCount = _files.Count
            _selectedFilesCount = _files.Where(Function(f) f.IsSelected).Count()
            _filesToRenameCount = _files.Where(Function(f) Not String.IsNullOrWhiteSpace(f.CustomPdfName) AndAlso
                                                      f.CustomPdfName <> Path.GetFileNameWithoutExtension(f.FileName)).Count()

            OnPropertyChanged(NameOf(TotalFilesCount))
            OnPropertyChanged(NameOf(SelectedFilesCount))
            OnPropertyChanged(NameOf(FilesToRenameCount))
            OnPropertyChanged(NameOf(FilesDisplayText))

            UpdatePublishButtonText()
        End Sub

        Private Sub UpdatePublishButtonText()
            Dim actionText = If(IsMergeEnabled, "Merge", "Export")
            _publishButtonText = $"🚀 {actionText} {_selectedFilesCount} Files"
            OnPropertyChanged(NameOf(PublishButtonText))
        End Sub

        ' Event handler for individual file selection
        Private Sub FileItem_PropertyChanged(sender As Object, e As PropertyChangedEventArgs)
            If e.PropertyName = NameOf(FileItemModel.IsSelected) OrElse e.PropertyName = NameOf(FileItemModel.CustomPdfName) Then
                UpdateFilesCountText()
            End If
        End Sub

        ' Selection manipulation
        Private Sub ToggleSelectDeselect()
            ' Detect current state first by casting to FileItemModel
            Dim allSelectedNow As Boolean = _filteredFilesView.Cast(Of FileItemModel)().
                                    All(Function(f) f.IsSelected)

            ' Toggle based on actual state
            _isAllSelected = Not allSelectedNow

            ' Apply selection
            For Each f As FileItemModel In _filteredFilesView
                f.IsSelected = _isAllSelected
            Next

            UpdateFilesCountText()

            ' Update button text
            SelectDeselectButtonText = If(_isAllSelected, "Deselect All", "Select All")

            OnPropertyChanged(NameOf(Files))
        End Sub

        Private Sub ClearFilters()
            CreatedDateStart = Nothing
            CreatedDateEnd = Nothing
            ModifiedDateStart = Nothing
            ModifiedDateEnd = Nothing
            SearchText = String.Empty
            RefreshFilter()
        End Sub

        Private Sub SelectAllFiles()
            For Each f As FileItemModel In _filteredFilesView
                f.IsSelected = True
            Next
            UpdateFilesCountText()
            OnPropertyChanged(NameOf(Files))
        End Sub

        Private Sub DeselectAllFiles()
            For Each f As FileItemModel In _filteredFilesView
                f.IsSelected = False
            Next
            UpdateFilesCountText()
            OnPropertyChanged(NameOf(Files))
        End Sub

        Private Sub InvertSelectionFiles()
            For Each f As FileItemModel In _filteredFilesView
                f.IsSelected = Not f.IsSelected
            Next
            UpdateFilesCountText()
            OnPropertyChanged(NameOf(Files))
        End Sub

        ' Publish files method
        Public Sub PublishFiles()
            If IsPublishing Then Return
            IsPublishing = True
            Try
                Dim selectedFiles = _files.Where(Function(f) f.IsSelected).ToList()
                If selectedFiles.Count = 0 Then
                    MessageBox.Show("No Files selected for publishing")
                    Log &= "No files selected for publishing." & Environment.NewLine
                    Return
                End If
                If String.IsNullOrEmpty(OutputPath) Then
                    MessageBox.Show("Please select an output folder before publishing")
                    Log &= "Please select an output folder before publishing." & Environment.NewLine
                    Return
                End If
                ' It checks for the filename duplication conflicts and throws error
                Dim overwriteAll As Boolean? = Nothing
                Dim conflictFiles As New List(Of String)
                Dim filesToExport As New List(Of FileItemModel)

                For Each file In selectedFiles
                    Dim pdfName As String = If(String.IsNullOrWhiteSpace(file.CustomPdfName),
                           Path.GetFileNameWithoutExtension(file.FileName),
                           Path.GetFileNameWithoutExtension(file.CustomPdfName)).ToUpperInvariant()

                    Dim pdfPath As String = Path.Combine(OutputPath, pdfName & ".pdf")

                    If System.IO.File.Exists(pdfPath) Then
                        If overwriteAll Is Nothing Then
                            Dim dlg As New FileConflictDialog(pdfName & ".pdf")
                            dlg.Owner = Application.Current.MainWindow
                            dlg.ShowDialog()
                            'Select case based on the user selection the skipping of files takes place
                            Select Case dlg.UserChoice
                                Case FileConflictDialog.ConflictResult.Yes
                                    filesToExport.Add(file) ' Add only this one
                                Case FileConflictDialog.ConflictResult.No
                                    file.Status = "Error: File exists, not overwritten."
                                    conflictFiles.Add(pdfName & ".pdf")
                                    Continue For
                                Case FileConflictDialog.ConflictResult.YesToAll
                                    overwriteAll = True
                                    filesToExport.Add(file)
                                Case FileConflictDialog.ConflictResult.NoToAll
                                    overwriteAll = False
                                    file.Status = "Error: File exists, not overwritten."
                                    conflictFiles.Add(pdfName & ".pdf")
                                    Continue For
                            End Select
                            'If the user wants to overwrite all the files
                        ElseIf overwriteAll = True Then
                            filesToExport.Add(file)
                        Else
                            file.Status = "Error: File exists, not overwritten."
                            conflictFiles.Add(pdfName & ".pdf")
                            Continue For
                        End If
                    Else
                        filesToExport.Add(file)
                    End If
                Next
                'It the user skips any files then it will show skipped files in  the messagebox
                If conflictFiles.Any() Then
                    MessageBox.Show("The following files were skipped due to overwrite being declined:" & vbCrLf &
    String.Join(vbCrLf, conflictFiles), "Skipped Files", MessageBoxButton.OK, MessageBoxImage.Warning)
                    Log &= "Skipped files: " & String.Join(", ", conflictFiles) & vbCrLf
                End If

                If filesToExport.Count = 0 Then
                    Log &= "No files to publish conflicting filenames." & vbCrLf
                    Return
                End If
                'Log &= $"Starting Creo launch and export for {selectedFiles.Count} files..." & Environment.NewLine
                'Command to export only selected files.
                ' Validate file accessibility before export
                Dim inaccessibleFiles As New List(Of FileItemModel)
                For Each file In filesToExport
                    If Not ValidateFileAccess(file.FullPath) Then
                        file.Status = "Error: File not accessible."
                        inaccessibleFiles.Add(file)
                    End If
                Next
                If inaccessibleFiles.Any() Then
                    For Each f In inaccessibleFiles
                        filesToExport.Remove(f)
                    Next
                    MessageBox.Show("The following files are not accessible and will be skipped:" & vbCrLf &
                                    String.Join(vbCrLf, inaccessibleFiles.Select(Function(f) f.FileName)),
                                    "Inaccessible Files", MessageBoxButton.OK, MessageBoxImage.Warning)
                End If
                If filesToExport.Count = 0 Then
                    Log &= "No accessible files to publish." & Environment.NewLine
                    Return
                End If

                Log &= $"Starting Creo launch and export for {filesToExport.Count} files..." & Environment.NewLine

                'Opening the working directory and creo executable path
                Dim workingDir = SettingsManager.Instance.WorkingDirectory
                Dim envLnkPath = Environment.GetEnvironmentVariable("PROE_PATH", EnvironmentVariableTarget.Machine)
                Dim savedCreoPath = OpenProeObjectClass.ResolveCreoPath(envLnkPath)
                If Not String.IsNullOrEmpty(savedCreoPath) AndAlso File.Exists(savedCreoPath) Then
                    Log &= $"Creo path resolved from PROE_PATH shortcut: {envLnkPath} → {savedCreoPath}{Environment.NewLine}"
                Else
                    savedCreoPath = My.Settings.CreoPath
                    Log &= $"Creo path taken from Settings: {savedCreoPath}{Environment.NewLine}"
                End If
                If String.IsNullOrEmpty(savedCreoPath) OrElse Not File.Exists(savedCreoPath) Then
                    MessageBox.Show("Invalid or missing Creo executable path. Please set it in Settings.")
                    Log &= "Invalid or missing Creo path." & Environment.NewLine
                    Return
                End If

                Dim launchSuccess = _openProe.RunProeWithPath(savedCreoPath, workingDir)
                'If creo launch fails.
                If Not launchSuccess Then
                    MessageBox.Show("Failed to launch Creo")
                    Log &= "Failed to launch Creo." & Environment.NewLine
                    Return
                End If
                'If initiliaze creo session is failed.
                CreoSessionManager.Instance.InitializeCreoSession()
                If CreoSessionManager.Instance.Session Is Nothing Then
                    MessageBox.Show("Failed to initialize Creo session")
                    Log &= "Failed to initialize Creo session." & Environment.NewLine
                    Return
                End If

                _proeCommonFuncs.setConfigOption("display_planes", "no")
                _proeCommonFuncs.setConfigOption("display_axes", "no")
                _proeCommonFuncs.setConfigOption("display_coord_sys", "no")

                Dim searchProPaths = SettingsManager.Instance.SearchProPath
                If Not String.IsNullOrWhiteSpace(searchProPaths) Then
                    For Each proFile In searchProPaths.Split(";"c).Select(Function(s) s.Trim()).Where(Function(s) s.Length > 0 AndAlso File.Exists(s))
                        For Each line In File.ReadAllLines(proFile)
                            Dim m = Regex.Match(line.Trim(), "^search_path\s+(.+)$", RegexOptions.IgnoreCase)
                            If m.Success Then
                                Dim dir = m.Groups(1).Value.Trim()
                                If Directory.Exists(dir) Then
                                    _proeCommonFuncs.setConfigOption("search_path", dir)
                                    Log &= $"Search path added: {dir}{Environment.NewLine}"
                                End If
                            End If
                        Next
                    Next
                End If
                ' Ask user about single vs. multiple PDF export

                _proeCommonFuncs.ExportAllDrawingsAsPDF(filesToExport, OutputPath, IsMergeEnabled)
                '_proeCommonFuncs.ExportAllDrawingsAsSTEP(filesToExport, OutputPath)

                _openProe.KillCreO()
                'After exporting all the selected files and publishing.
                For Each file In selectedFiles
                    OnPropertyChanged(NameOf(Files))
                Next
                Log &= "Publishing complete." & Environment.NewLine

                '  Export report
                ExportReportToCsv(selectedFiles)
                MessageBox.Show($"PDF export completed suc cessfully for {filesToExport.Count} files!" & vbCrLf &
                                "A detailed report has been saved in the output folder.",
                                "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information)

                If IsCliMode Then
                    Try
                        Dim logFolder = Path.Combine(OutputPath, "Logs")
                        Directory.CreateDirectory(logFolder)
                        Dim logFile = Path.Combine(logFolder, $"CLIPublishLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt")
                        File.WriteAllText(logFile, Log)
                    Catch ex As Exception
                        ' Ignore logging errors in CLI
                    End Try

                End If
            Finally
                IsPublishing = False
            End Try
        End Sub
        ' Open settings
        Private Sub OpenSettingsWindow()
            Dim settingsWindow As New SettingsWindow()
            settingsWindow.DataContext = New SettingsWindowViewModel()
            settingsWindow.ShowDialog()
        End Sub

        ' Open license management
        Private Sub OpenLicenseSettingsWindow()
            Dim licenseSettingsWindow As New LicenseSettingsWindow()
            licenseSettingsWindow.DataContext = New LicenseSettingsViewModel()
            licenseSettingsWindow.ShowDialog()
        End Sub

        ' INotifyPropertyChanged implementation
        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
        Protected Sub OnPropertyChanged(<CallerMemberName> Optional propertyName As String = Nothing)
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
        End Sub

        'Adds files and folders dropped into the app.
        ' Accepts .drw files and .drw files inside dropped folders (non-recursive).
        ''' <param name="paths">Array of dropped paths (files or folders).</param>
        Public Sub AddDroppedFiles(paths() As String)
            Try
                Dim droppedFolders As New List(Of String)()
                Dim droppedFiles As New List(Of String)()
                Dim addedFilesCount As Integer = 0

                'Log &= $"Dropped items count: {paths.Length}{Environment.NewLine}"

                ' Separate files and folders first
                For Each path As String In paths
                    Log &= $"Dropped Files From : {path}{Environment.NewLine}"

                    If Directory.Exists(path) Then
                        droppedFolders.Add(path)
                    ElseIf File.Exists(path) Then
                        droppedFiles.Add(path)
                    End If
                Next

                ' Add individual files first
                For Each filePath In droppedFiles
                    Dim extension = System.IO.Path.GetExtension(filePath)
                    If extension.Equals(".drw", StringComparison.OrdinalIgnoreCase) Then
                        AddFileIfNotExists(filePath)
                        addedFilesCount += 1
                    End If
                Next

                ' Add folders only if they weren't redundant with dropped files
                For Each folderPath In droppedFolders
                    ' Check if any dropped file is inside this folder to avoid duplicate adding
                    Dim hasFileInside = droppedFiles.Any(Function(f) f.StartsWith(folderPath.TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    If hasFileInside Then
                        Log &= $"Skipped folder {folderPath} because its file(s) were already added.{Environment.NewLine}"
                        Continue For
                    End If

                    Dim drwFiles = Directory.GetFiles(folderPath, "*.drw", SearchOption.AllDirectories)

                    For Each file In drwFiles
                        AddFileIfNotExists(file)
                        addedFilesCount += 1
                    Next
                Next

                RefreshFilter()
                UpdateFilesCountText()

                OnPropertyChanged(NameOf(Files))
                OnPropertyChanged(NameOf(FilteredFiles))
                UpdateCounts()
                Log &= $"Total files added from drop: {addedFilesCount}{Environment.NewLine}"
            Catch ex As Exception
                ErrorLogger.LogException("Add dropped files", ex)
                Log &= $"Error adding dropped files: {ErrorLogger.FormatBrief(ex)} (details: {ErrorLogger.LogFilePath}){Environment.NewLine}"
            End Try
        End Sub

        ' Adds a file to the _files collection if it does not already exist.
        ''' <param name="filePath">Full file path</param>
        Private Sub AddFileIfNotExists(filePath As String)
            If Not _files.Any(Function(f) f.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)) Then
                Dim fi As New FileInfo(filePath)
                Dim fileModel As New FileItemModel(filePath)
                AddHandler fileModel.PropertyChanged, AddressOf FileItem_PropertyChanged
                _files.Add(fileModel)

            End If
            CompleteStep(3) ' Mark step 1 as completed
        End Sub

        'Export to CSV File Method
        Private Sub ExportReportToCsv(files As List(Of FileItemModel))
            Try
                Dim timestamp As String = DateTime.Now.ToString("yyyyMMdd_HHmmss")
                Dim generatedTime As String = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")

                ' ==== CSV EXPORT ====
                Dim csvPath = Path.Combine(OutputPath, $"BulkPDFPublisherReport_{timestamp}.csv")
                Using writer As New StreamWriter(csvPath, False)

                    ' ==== TITLE BLOCK ====
                    writer.WriteLine("Adroitec Engineering Solutions Pvt. Ltd")
                    writer.WriteLine("Bulk PDF Publisher - Report")
                    writer.WriteLine("Generated On:," & generatedTime)
                    writer.WriteLine("Report Version: 1.0")
                    writer.WriteLine(New String("-"c, 80))
                    writer.WriteLine()

                    ' ==== COLUMN HEADERS ====
                    writer.WriteLine("File Name,Custom PDF Name,Status,Created Date,Modified Date,File Size (KB),PDF Generated Timestamp")

                    ' ==== ROWS ====
                    For Each file In files
                        Dim line As String = String.Join(",",
                    $"""{file.FileName}""",
                    $"""{file.CustomPdfName}""",
                    $"""{file.Status}""",
                    file.CreatedDate,
                    file.ModifiedDate,
                    file.FileSize,
                    $"""{generatedTime}""")
                        writer.WriteLine(line)
                    Next

                    writer.WriteLine()

                    ' ==== FOOTER SUMMARY ====
                    Dim totalFiles = files.Count
                    Dim successCount As Integer = System.Linq.Enumerable.Count(files,
                    Function(f) Not String.IsNullOrEmpty(f.Status) AndAlso
                f.Status.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0)
                    Dim failCount = totalFiles - successCount
                    writer.WriteLine(New String("-"c, 80))
                    writer.WriteLine($"Summary, Total Files: {totalFiles}, Success: {successCount}, Failed: {failCount}")
                    writer.WriteLine(New String("-"c, 80))

                    ' ==== PUBLISHING LOG ====
                    writer.WriteLine()
                    writer.WriteLine("Publishing Log")
                    writer.WriteLine(New String("-"c, 80))
                    For Each logLine In _log.Split({Environment.NewLine}, StringSplitOptions.None)
                        Dim escaped = logLine.Replace("""", """""")
                        writer.WriteLine("""" & escaped & """")
                    Next
                    writer.WriteLine(New String("-"c, 80))
                End Using

                ' Update tracking properties
                _lastExportedCsvPath = csvPath
                ReportFilePath = csvPath

                ' Log info
                Log &= $"Last exported CSV path stored as: {_lastExportedCsvPath}{Environment.NewLine}"
                Log &= $"CSV report saved to: {csvPath}{Environment.NewLine}"

                ' === Notify OpenReportCommand CanExecute changed ===
                If _OpenReportCommand IsNot Nothing AndAlso TypeOf _OpenReportCommand Is RelayCommand Then
                    CType(_OpenReportCommand, RelayCommand).RaiseCanExecuteChanged()
                End If

            Catch ex As Exception
                ErrorLogger.LogException("Generate CSV report", ex)
                Log &= $"Error generating report: {ErrorLogger.FormatBrief(ex)} (details: {ErrorLogger.LogFilePath}){Environment.NewLine}"
            End Try

            CompleteStep(4) ' Mark step 4 as completed
        End Sub
        Private Sub ExportReportCommandExecute(obj As Object)
            ' Assuming Files is ObservableCollection(Of FileItemModel)
            ExportReportToCsv(Files.ToList())
        End Sub
        Private Function CanOpenReport(obj As Object) As Boolean
            Return Not String.IsNullOrEmpty(_lastExportedCsvPath) AndAlso File.Exists(_lastExportedCsvPath)
        End Function

        Private Sub OpenReportCommandExecute(obj As Object)
            Try
                If CanOpenReport(obj) Then
                    Process.Start("notepad.exe", _lastExportedCsvPath)
                Else
                    MessageBox.Show("No exported report found. Please export the report first.", "Info", MessageBoxButton.OK, MessageBoxImage.Information)
                End If
            Catch ex As Exception
                ErrorLogger.LogException("Open CSV report", ex)
                Log &= $"Error opening CSV report: {ErrorLogger.FormatBrief(ex)}{Environment.NewLine}"
            End Try
        End Sub
        Private Sub UpdateWorkingDirectory(directoryPath As String)
            Try
                ' Update the SettingsManager instance
                SettingsManager.Instance.WorkingDirectory = directoryPath

                'Optionally update My.Settings as well (uncomment if you want to persist immediately)
                My.Settings.WorkingDirectory = directoryPath
                'My.Settings.Save()

                Log &= $"Working directory updated to: {directoryPath}{Environment.NewLine}"
            Catch ex As Exception
                ErrorLogger.LogException($"Update working directory: {directoryPath}", ex)
                Log &= $"Error updating working directory: {ErrorLogger.FormatBrief(ex)}{Environment.NewLine}"
            End Try
        End Sub
        Public Sub CompleteStep(stepNumber As Integer)
            Select Case stepNumber
                Case 1
                    IsStep1Completed = True
                Case 2
                    IsStep2Completed = True
                Case 3
                    IsStep3Completed = True
                Case 4

                    IsStep4Completed = True
            End Select
        End Sub
        Private Sub OpenReport(param As Object)
            Try
                Process.Start(New ProcessStartInfo(ReportFilePath) With {
                    .UseShellExecute = True
                })
            Catch ex As Exception
                ErrorLogger.LogException("Open report file", ex)
                Log &= $"Could not open report: {ErrorLogger.FormatBrief(ex)}{Environment.NewLine}"
            End Try
        End Sub

        ' ---------- Error log access ----------
        Public ReadOnly Property ErrorLogPath As String
            Get
                Return ErrorLogger.LogFilePath
            End Get
        End Property

        Private _openErrorLogCommand As ICommand
        Public Property OpenErrorLogCommand As ICommand
            Get
                If _openErrorLogCommand Is Nothing Then
                    _openErrorLogCommand = New RelayCommand(AddressOf OpenErrorLogExecute)
                End If
                Return _openErrorLogCommand
            End Get
            Set(value As ICommand)
                _openErrorLogCommand = value
            End Set
        End Property

        Private Sub OpenErrorLogExecute(obj As Object)
            Try
                Dim path = ErrorLogger.LogFilePath
                If Not File.Exists(path) Then
                    File.WriteAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] No errors have been logged yet.{Environment.NewLine}")
                End If
                Process.Start(New ProcessStartInfo(path) With {.UseShellExecute = True})
            Catch ex As Exception
                MessageBox.Show("Could not open the error log: " & ex.Message, "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End Sub

    End Class

End Namespace