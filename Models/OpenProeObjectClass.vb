Imports pfcls
Imports System.IO
Imports System.Windows

Public Class OpenProeObjectClass
    Private _asyncConn As IpfcAsyncConnection

    ' Updated method using saved path
    Public Function RunProeWithPath(proeExePath As String, workingDir As String) As Boolean
        Try
            Dim creoProcesses = System.Diagnostics.Process.GetProcessesByName("xtop")
            If creoProcesses.Length > 0 Then
                MsgBox("Creo is already running. Please close it before proceeding.", MsgBoxStyle.Exclamation, "CREO Already Running")
                Return False
            End If
            If Not File.Exists(proeExePath) Then
                MessageBox.Show("Creo executable not found.")
                Return False
            End If
            Environment.SetEnvironmentVariable("PROE_PATH", proeExePath, EnvironmentVariableTarget.User)
            Dim launcher As CCpfcAsyncConnection = New CCpfcAsyncConnection()
            _asyncConn = launcher.Start(proeExePath, workingDir)

            Return True
        Catch ex As Exception
            MessageBox.Show("Launch Error: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function ResolveCreoPath(path As String) As String
        If String.IsNullOrEmpty(path) Then Return path
        If Not path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) Then Return path
        Try
            Dim shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"))
            Dim shortcut = shell.GetType().InvokeMember("CreateShortcut", Reflection.BindingFlags.InvokeMethod, Nothing, shell, New Object() {path})
            Dim target = CStr(shortcut.GetType().InvokeMember("TargetPath", Reflection.BindingFlags.GetProperty, Nothing, shortcut, Nothing))
            Return If(Not String.IsNullOrEmpty(target), target, path)
        Catch
            Return path
        End Try
    End Function

    Public Sub KillCreO()
        Dim proc As System.Diagnostics.Process
        Try
            For Each proc In System.Diagnostics.Process.GetProcessesByName("xtop")

                If proc.HasExited = False Then
                    If proc.Responding = False Then
                        proc.Kill()
                    End If
                    proc.Kill()
                End If
            Next
            For Each proc In System.Diagnostics.Process.GetProcessesByName("nmsd")
                If proc.HasExited = False Then
                    proc.Kill()
                End If
            Next
            For Each proc In System.Diagnostics.Process.GetProcessesByName("pfcls")
                If proc.HasExited = False Then
                    proc.Kill()
                End If
            Next
        Catch ex As Exception
        End Try
    End Sub
End Class
