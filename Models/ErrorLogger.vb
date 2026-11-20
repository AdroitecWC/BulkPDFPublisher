Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text

''' <summary>
''' Central error log for the app. Catch-blocks across the UI only ever see the
''' top-level exception message, which for Creo Toolkit calls is often just
''' "Toolkit error" with the actual cause buried in HRESULT, InnerException or
''' the pfcls COM layer. This writes the full picture -- type, HRESULT, inner
''' chain, stack -- to a dedicated file so a user can hand the log to support
''' instead of guessing what "Toolkit error" meant.
''' </summary>
Public Module ErrorLogger

    Private ReadOnly _lock As New Object()

    ''' <summary>Full path of the error log file. Kept under %APPDATA% so a
    ''' non-admin user can always write to it (same reasoning as the license
    ''' store).</summary>
    Public ReadOnly Property LogFilePath As String
        Get
            Dim dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Adroitec Engineering Solutions Pvt Ltd", "BulkPDFPublisher", "Logs")
            If Not Directory.Exists(dir) Then Directory.CreateDirectory(dir)
            Return Path.Combine(dir, "errors.log")
        End Get
    End Property

    ''' <summary>Log a caught exception with contextual info about what was being
    ''' attempted (e.g. "Export PDF: part123.drw"). Writes exception type,
    ''' HRESULT, message, every inner exception, and the stack trace.</summary>
    Public Sub LogException(context As String, ex As Exception)
        Try
            Dim sb As New StringBuilder()
            sb.AppendLine("================================================================")
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR  {context}")
            AppendExceptionChain(sb, ex)
            WriteLog(sb.ToString())
        Catch
            ' Logging must never itself throw.
        End Try
    End Sub

    ''' <summary>Log a plain message (warning / info the user should still be
    ''' able to see later). category is a free-form tag like "Creo", "App",
    ''' "License".</summary>
    Public Sub LogMessage(category As String, message As String)
        Try
            WriteLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {category.ToUpperInvariant()}  {message}{Environment.NewLine}")
        Catch
        End Try
    End Sub

    ''' <summary>One-line, user-facing summary of an exception -- type + message
    ''' + HRESULT when it's a COM failure (Creo Toolkit calls surface via
    ''' COMException, and the HRESULT is usually the only hint of the actual
    ''' cause). Safe to put in the on-screen log textbox or a row Status.</summary>
    Public Function FormatBrief(ex As Exception) As String
        If ex Is Nothing Then Return "Unknown error"
        Dim msg = If(String.IsNullOrWhiteSpace(ex.Message), ex.GetType().Name, ex.Message)
        Dim com = TryCast(ex, COMException)
        If com IsNot Nothing Then
            Return $"{msg} (HRESULT 0x{com.HResult:X8})"
        End If
        Return msg
    End Function

    Private Sub AppendExceptionChain(sb As StringBuilder, ex As Exception)
        Dim cur = ex
        Dim depth = 0
        While cur IsNot Nothing
            Dim prefix = If(depth = 0, "Exception", $"Inner[{depth}]")
            sb.AppendLine($"{prefix}: {cur.GetType().FullName}")
            sb.AppendLine($"  Message : {cur.Message}")
            sb.AppendLine($"  HRESULT : 0x{cur.HResult:X8}")
            Dim com = TryCast(cur, COMException)
            If com IsNot Nothing Then
                sb.AppendLine($"  Source  : {com.Source}")
            End If
            If Not String.IsNullOrEmpty(cur.StackTrace) Then
                sb.AppendLine("  Stack:")
                For Each line In cur.StackTrace.Split({Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
                    sb.AppendLine("    " & line.TrimStart())
                Next
            End If
            cur = cur.InnerException
            depth += 1
        End While
    End Sub

    Private Sub WriteLog(text As String)
        SyncLock _lock
            File.AppendAllText(LogFilePath, text)
        End SyncLock
    End Sub

End Module
