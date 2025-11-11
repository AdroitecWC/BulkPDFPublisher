Imports System.IO
Imports System.Text
Imports System.Security.Cryptography
Imports Microsoft.Win32
Imports System.Management


Public Module LicenseManager
    Private Const LicfilePwd As String = "Kbe@Adr"
    Private Const LicenseFilePath As String = "Resources\License\License.lic"
    Private Const RegPath As String = "Software\MyCompany\MyTool"

    Public Function CheckLicense() As Boolean
        Dim fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LicenseFilePath)
        Dim tempPath = Path.GetTempPath() & "TempLicCheck.txt"

        If Not File.Exists(fullPath) Then Return False

        ' Decrypt License.lic
        Dim key = CreateKey(LicfilePwd)
        Dim iv = CreateIV(LicfilePwd)
        Try
            EncryptOrDecryptFile(fullPath, tempPath, key, iv, CryptoAction.ActionDecrypt)
        Catch
            Return False
        End Try

        ' Read decrypted license
        Try
            Using reader As New StreamReader(tempPath)
                Dim expiry = reader.ReadLine().Replace("LastDate:", "").Trim()
                If expiry <> "Nil" AndAlso DateTime.TryParse(expiry, Nothing) Then
                    If DateTime.Parse(expiry) < DateTime.Today Then Return False
                End If
                ' Validate request code
                Dim expectedCode = GenerateLicenseCode()
                For Each ch In expectedCode
                    If reader.EndOfStream Then Return False
                    Dim val = reader.ReadLine()
                    If Asc(ch) <> Integer.Parse(val) Then Return False
                Next
            End Using
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Function GenerateLicenseCode() As String
        Dim rCode = GetSystemInfo()
        Return rCode
    End Function

    Private Function GetSystemInfo() As String
        Dim info = ""
        info &= GetWMI("Win32_Processor", "ProcessorId")
        info &= GetWMI("Win32_BaseBoard", "Product")
        info &= GetWMI("Win32_DiskDrive", "Signature")
        info &= GetWMI("Win32_BIOS", "Version")
        Return New String(info.Where(Function(c) Char.IsLetterOrDigit(c)).ToArray())
    End Function

    Private Function GetWMI(className As String, propertyName As String) As String
        Try
            Dim searcher As New ManagementObjectSearcher($"SELECT * FROM {className}")
            For Each obj In searcher.Get()
                If obj(propertyName) IsNot Nothing Then
                    Return obj(propertyName).ToString()
                End If
            Next
        Catch
        End Try
        Return ""
    End Function

    Public Enum CryptoAction
        ActionEncrypt = 1
        ActionDecrypt = 2
    End Enum

    Public Function CreateKey(password As String) As Byte()
        Dim hash = New SHA512Managed().ComputeHash(Encoding.ASCII.GetBytes(password))
        Return hash.Take(32).ToArray()
    End Function

    Public Function CreateIV(password As String) As Byte()
        Dim hash = New SHA512Managed().ComputeHash(Encoding.ASCII.GetBytes(password))
        Return hash.Skip(32).Take(16).ToArray()
    End Function

    Public Sub EncryptOrDecryptFile(inputPath As String, outputPath As String, key() As Byte, iv() As Byte, direction As CryptoAction)
        Using fsInput As New FileStream(inputPath, FileMode.Open, FileAccess.Read)
            Using fsOutput As New FileStream(outputPath, FileMode.Create, FileAccess.Write)
                Dim csp = New RijndaelManaged()
                Dim cs As CryptoStream
                If direction = CryptoAction.ActionEncrypt Then
                    cs = New CryptoStream(fsOutput, csp.CreateEncryptor(key, iv), CryptoStreamMode.Write)
                Else
                    cs = New CryptoStream(fsOutput, csp.CreateDecryptor(key, iv), CryptoStreamMode.Write)
                End If

                Dim buffer(4096) As Byte
                Dim bytesRead As Integer
                Do
                    bytesRead = fsInput.Read(buffer, 0, buffer.Length)
                    If bytesRead = 0 Then Exit Do
                    cs.Write(buffer, 0, bytesRead)
                Loop
                cs.Close()
            End Using
        End Using
    End Sub
End Module
