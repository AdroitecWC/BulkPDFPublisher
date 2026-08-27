Imports System.IO
Imports System.Text
Imports System.Security.Cryptography
Imports Microsoft.Win32
Imports System.Management

Public Module LicenseManager
    Public Enum LicenseError
        None
        LicenseFileMissing
        NotActivated
        Expired
        LicenseTransferred
        MachineMismatch
        LicenseTampered
        CryptoError
    End Enum

    Private Const LicfilePwd As String = "Kbe@Adr"
    Private Const UsedLicensesRegPath As String = "Software\MyCompany\MyTool\UsedLicenses"

    ' =====================================================
    ' LICENSE STORAGE LOCATION
    ' =====================================================
    Public Function GetLicensePath() As String
        Dim basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        Dim folder = Path.Combine(basePath, "MyCompany", "BulkPDFPublisher", "License")

        If Not Directory.Exists(folder) Then
            Directory.CreateDirectory(folder)
        End If

        Return Path.Combine(folder, "License.lic")
    End Function

    ' =====================================================
    ' CHECK LICENSE (ACTIVE LICENSE ONLY)
    ' =====================================================
    Public Function CheckLicense() As Boolean
        Dim fullPath = GetLicensePath()
        If Not File.Exists(fullPath) Then Return False

        Dim tempPath = Path.GetTempFileName()

        Try
            Decrypt(fullPath, tempPath)
            Dim data = ReadLicense(tempPath)

            ' Must be activated
            If Not data.ContainsKey("Activated") OrElse data("Activated") <> "True" Then
                Return False
            End If

            ' Expiry
            If data.ContainsKey("LastDate") AndAlso data("LastDate") <> "Nil" Then
                Dim exp As DateTime
                If DateTime.TryParse(data("LastDate"), exp) AndAlso exp < DateTime.Today Then
                    Return False
                End If
            End If

            Dim currentSystemCode = GenerateLicenseCode()
            If data.ContainsKey("LicenseUid") AndAlso
   IsLicenseUsed("TRANSFERRED_LIC_" & data("LicenseUid")) Then
                Return False
            End If


            ' ❌ Must belong to this system
            If data("LicenseId") <> currentSystemCode Then
                Return False
            End If

            ' Character binding
            For i = 0 To currentSystemCode.Length - 1
                Dim key = $"C{i}"
                If Not data.ContainsKey(key) OrElse
                   Asc(currentSystemCode(i)) <> Integer.Parse(data(key)) Then
                    Return False
                End If
            Next

            Return True

        Catch
            Return False
        Finally
            Try : File.Delete(tempPath) : Catch : End Try
        End Try
    End Function

    ' =====================================================
    ' ACTIVATE LICENSE (FIRST TIME)
    ' =====================================================
    Public Function ActivateLicense() As Boolean
        Dim fullPath = GetLicensePath()
        If Not File.Exists(fullPath) Then Return False

        Dim tempPath = Path.GetTempFileName()

        Try
            Decrypt(fullPath, tempPath)
            Dim data = ReadLicense(tempPath)

            Dim currentSystemCode = GenerateLicenseCode()

            If data.ContainsKey("LicenseUid") AndAlso
   IsLicenseUsed("TRANSFERRED_LIC_" & data("LicenseUid")) Then
                Return False
            End If


            ' ❌ Must belong to this system
            If data("LicenseId") <> currentSystemCode Then
                Return False
            End If

            data("Activated") = "True"
            data("Transferred") = "False"

            WriteLicense(tempPath, data)
            Encrypt(tempPath, fullPath)

            Return True

        Catch
            Return False
        Finally
            Try : File.Delete(tempPath) : Catch : End Try
        End Try
    End Function

    ' =====================================================
    ' DEACTIVATE LICENSE (NO PERMANENT BLOCK)
    ' =====================================================
    Public Function DeactivateLicense() As Boolean
        Dim fullPath = GetLicensePath()
        If Not File.Exists(fullPath) Then Return False

        Dim tempPath = Path.GetTempFileName()

        Try
            Decrypt(fullPath, tempPath)
            Dim data = ReadLicense(tempPath)

            data("Activated") = "False"

            WriteLicense(tempPath, data)
            Encrypt(tempPath, fullPath)

            Return True

        Catch
            Return False
        Finally
            Try : File.Delete(tempPath) : Catch : End Try
        End Try
    End Function

    ' =====================================================
    ' TRANSFER LICENSE (A → B)
    ' =====================================================
    Public Function TransferLicense(newSystemCode As String,
                                    savePath As String) As Boolean

        Dim fullPath = GetLicensePath()
        If Not File.Exists(fullPath) Then Return False

        Dim tempPath = Path.GetTempFileName()

        Try
            Decrypt(fullPath, tempPath)
            Dim data = ReadLicense(tempPath)

            ' Already transferred
            If data.ContainsKey("Transferred") AndAlso data("Transferred") = "True" Then
                Return False
            End If

            ' 🔒 Kill original license permanently
            data("Activated") = "False"
            data("Transferred") = "True"

            WriteLicense(tempPath, data)
            Encrypt(tempPath, fullPath)

            ' 🔐 Block ORIGINAL license forever
            MarkLicenseUsed("TRANSFERRED_LIC_" & data("LicenseUid"))


            ' 🆕 Create new license for target system
            Dim newData As New Dictionary(Of String, String)

            For Each kv In data
                newData(kv.Key) = kv.Value
            Next

            newData("LicenseId") = newSystemCode
            newData("Activated") = "True"
            newData("Transferred") = "False"

            ' Rebind C0..Cn
            For Each k In newData.Keys.Where(Function(x) x.StartsWith("C")).ToList()
                newData.Remove(k)
            Next

            For i = 0 To newSystemCode.Length - 1
                newData($"C{i}") = Asc(newSystemCode(i)).ToString()
            Next

            WriteLicense(tempPath, newData)
            Encrypt(tempPath, savePath)

            Return True

        Catch
            Return False
        Finally
            Try : File.Delete(tempPath) : Catch : End Try
        End Try
    End Function

    ' =====================================================
    ' FILE IO
    ' =====================================================
    Private Function ReadLicense(path As String) As Dictionary(Of String, String)
        Dim dict As New Dictionary(Of String, String)
        For Each line In File.ReadAllLines(path)
            If line.Contains(":") Then
                Dim p = line.Split(":"c, 2)
                dict(p(0).Trim()) = p(1).Trim()
            End If
        Next
        Return dict
    End Function

    Private Sub WriteLicense(path As String, data As Dictionary(Of String, String))
        Using w As New StreamWriter(path, False)
            For Each kv In data
                w.WriteLine($"{kv.Key}:{kv.Value}")
            Next
        End Using
    End Sub

    ' =====================================================
    ' REGISTRY
    ' =====================================================
    Private Function IsLicenseUsed(id As String) As Boolean
        Using k = Registry.CurrentUser.OpenSubKey(UsedLicensesRegPath)
            Return k IsNot Nothing AndAlso k.GetValue(id) IsNot Nothing
        End Using
    End Function

    Private Sub MarkLicenseUsed(id As String)
        Using k = Registry.CurrentUser.CreateSubKey(UsedLicensesRegPath)
            k.SetValue(id, DateTime.Now.ToString("s"))
        End Using
    End Sub

    ' =====================================================
    ' MACHINE CODE
    ' =====================================================
    Public Function GenerateLicenseCode() As String
        Dim raw =
            GetWMI("Win32_Processor", "ProcessorId") &
            GetWMI("Win32_BaseBoard", "Product") &
            GetWMI("Win32_DiskDrive", "Signature") &
            GetWMI("Win32_BIOS", "Version")

        Return New String(raw.Where(Function(c) Char.IsLetterOrDigit(c)).ToArray())
    End Function

    Private Function GetWMI(cls As String, prop As String) As String
        Try
            For Each o In New ManagementObjectSearcher($"SELECT * FROM {cls}").Get()
                If o(prop) IsNot Nothing Then Return o(prop).ToString()
            Next
        Catch
        End Try
        Return ""
    End Function

    ' =====================================================
    ' CRYPTO
    ' =====================================================
    Private Sub Encrypt(input As String, output As String)
        Crypto(input, output, True)
    End Sub

    Private Sub Decrypt(input As String, output As String)
        Crypto(input, output, False)
    End Sub

    Private Sub Crypto(input As String, output As String, encrypt As Boolean)
        Dim key = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(LicfilePwd))

        Using aes As New RijndaelManaged()
            aes.Key = key.Take(32).ToArray()
            aes.IV = key.Skip(32).Take(16).ToArray()

            Using fsIn As New FileStream(input, FileMode.Open)
                Using fsOut As New FileStream(output, FileMode.Create)
                    Using cs As New CryptoStream(
                        fsOut,
                        If(encrypt, aes.CreateEncryptor(), aes.CreateDecryptor()),
                        CryptoStreamMode.Write)
                        fsIn.CopyTo(cs)
                    End Using
                End Using
            End Using
        End Using
    End Sub

End Module
