Imports System.IO
Imports IOPath = System.IO.Path
Imports System.Text
Imports System.Security.Cryptography
Imports Microsoft.Win32
Imports System.Management

Public Module LicenseManager

    Public Enum LicenseError
        None                ' License is valid
        LicenseFileMissing  ' .lic file not found
        NotActivated        ' Activated flag is False
        Expired             ' LastDate is in the past
        LicenseTransferred  ' License was transferred away from this machine
        MachineMismatch     ' LicenseId does not match this hardware
        LicenseTampered     ' Decryption failed / file corrupt
        CryptoError         ' Unexpected crypto exception
        WrongProduct        ' License is for a different product
    End Enum

    Private Const LicfilePwd As String = "Kbe@Adr"
    Public Const AppProduct As String = "Bulk PDF publish"
    ' Scoped by ToolName -- these paths used to be the literal, identical string across
    ' every Adroitec tool ("...\MyTool\..."), so activating one tool with a License Key
    ' would overwrite the same registry value another tool reads back, making it look
    ' like "every tool accepts the same key". Each tool now gets its own subkey.
    Private Const UsedLicensesRegPath As String = "Software\Adroitec Engineering Solutions Pvt Ltd\BulkPDFPublisher\UsedLicenses"

    ' =====================================================
    ' PUBLIC API
    ' =====================================================

    ''' <summary>True if the license is valid.</summary>
    Public Function CheckLicense() As Boolean
        Return CheckLicenseDetailed() = LicenseError.None
    End Function

    ''' <summary>Returns the specific reason for failure, or None on success.</summary>
    Public Function CheckLicenseDetailed() As LicenseError
        Dim fullPath = GetLicensePath()
        If Not File.Exists(fullPath) Then Return LicenseError.LicenseFileMissing

        Dim data As Dictionary(Of String, String)
        Try
            data = ReadLicense(DecryptFile(fullPath))
        Catch
            Return LicenseError.CryptoError
        End Try

        Try
            ' Must be activated
            If Not data.ContainsKey("Activated") OrElse data("Activated") <> "True" Then
                Return LicenseError.NotActivated
            End If

            ' Product match
            If data.ContainsKey("Product") Then
                Dim licProduct = data("Product")
                If Not licProduct.StartsWith(AppProduct, StringComparison.OrdinalIgnoreCase) Then
                    Return LicenseError.WrongProduct
                End If
            End If

            ' Expiry check
            If data.ContainsKey("LastDate") AndAlso data("LastDate") <> "Nil" Then
                Dim exp As DateTime
                If DateTime.TryParse(data("LastDate"), exp) AndAlso exp < DateTime.Today Then
                    Return LicenseError.Expired
                End If
            End If

            ' ── NODE-LOCKED LICENSE ──────────────────────────────────────────
            Dim currentSystemCode = GenerateLicenseCode()

            If data.ContainsKey("LicenseUid") AndAlso
               IsLicenseUsed("TRANSFERRED_LIC_" & data("LicenseUid")) Then
                Return LicenseError.LicenseTransferred
            End If

            If Not data.ContainsKey("LicenseId") OrElse
               data("LicenseId") <> currentSystemCode Then
                Return LicenseError.MachineMismatch
            End If

            ' Character-by-character binding
            For i = 0 To currentSystemCode.Length - 1
                Dim key = $"C{i}"
                If Not data.ContainsKey(key) OrElse
                   Asc(currentSystemCode(i)) <> Integer.Parse(data(key)) Then
                    Return LicenseError.MachineMismatch
                End If
            Next

            Return LicenseError.None

        Catch
            Return LicenseError.LicenseTampered
        End Try
    End Function

    ' =====================================================
    ' ACTIVATE LICENSE (FIRST TIME — node-locked)
    ' =====================================================
    Public Function ActivateLicense() As Boolean
        Dim fullPath = GetLicensePath()
        If Not File.Exists(fullPath) Then Return False

        Try
            Dim data = ReadLicense(DecryptFile(fullPath))

            Dim currentSystemCode = GenerateLicenseCode()

            If data.ContainsKey("LicenseUid") AndAlso
               IsLicenseUsed("TRANSFERRED_LIC_" & data("LicenseUid")) Then
                Return False
            End If

            If Not data.ContainsKey("LicenseId") OrElse
               data("LicenseId") <> currentSystemCode Then
                Return False
            End If

            data("Activated") = "True"
            data("Transferred") = "False"
            EncryptToFile(SerializeLicense(data), fullPath)
            Return True

        Catch
            Return False
        End Try
    End Function

    ' =====================================================
    ' DEACTIVATE LICENSE
    ' =====================================================
    Public Function DeactivateLicense() As Boolean
        Dim fullPath = GetLicensePath()
        If Not File.Exists(fullPath) Then Return False

        Try
            Dim data = ReadLicense(DecryptFile(fullPath))
            data("Activated") = "False"
            EncryptToFile(SerializeLicense(data), fullPath)
            Return True
        Catch
            Return False
        End Try
    End Function

    ' =====================================================
    ' TRANSFER LICENSE (node-locked, one-time)
    ' =====================================================
    Public Function TransferLicense(newSystemCode As String, savePath As String) As Boolean
        Dim fullPath = GetLicensePath()
        If Not File.Exists(fullPath) Then Return False

        Try
            Dim data = ReadLicense(DecryptFile(fullPath))

            If data.ContainsKey("Transferred") AndAlso data("Transferred") = "True" Then
                Return False
            End If

            data("Activated") = "False"
            data("Transferred") = "True"
            EncryptToFile(SerializeLicense(data), fullPath)
            MarkLicenseUsed("TRANSFERRED_LIC_" & data("LicenseUid"))

            Dim newData As New Dictionary(Of String, String)
            For Each kv In data
                newData(kv.Key) = kv.Value
            Next
            newData("LicenseId") = newSystemCode
            newData("Activated") = "True"
            newData("Transferred") = "False"

            For Each k In newData.Keys.Where(Function(x) x.StartsWith("C")).ToList()
                newData.Remove(k)
            Next
            For i = 0 To newSystemCode.Length - 1
                newData($"C{i}") = Asc(newSystemCode(i)).ToString()
            Next

            EncryptToFile(SerializeLicense(newData), savePath)
            Return True

        Catch
            Return False
        End Try
    End Function

    ' =====================================================
    ' LICENSE PATH
    ' =====================================================
    ''' <summary>
    ''' Per-user, always-writable license storage under %APPDATA% -- NOT next to the exe.
    ''' A dev build's exe folder happens to be writable, which masked this for a long
    ''' time, but an MSI installs into C:\Program Files (x86)\..., which a normal
    ''' (non-admin) user cannot write to, so creating "Resources\License\" there throws
    ''' UnauthorizedAccessException on first launch -- exactly the "Access to the path
    ''' ... is denied" crash this was hit with. One-time migration: if an old license
    ''' file sits next to the exe from a pre-fix install, copy it here so re-activation
    ''' isn't required.
    ''' </summary>
    Public Function GetLicensePath() As String
        Dim path = IOPath.Combine(GetLicenseDataDir(), "License.lic")
        MigrateLegacyLicenseFileIfNeeded("License.lic", path)
        Return path
    End Function

    Private Function GetLicenseDataDir() As String
        Dim dir = IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Adroitec Engineering Solutions Pvt Ltd", "BulkPDFPublisher", "License")
        If Not Directory.Exists(dir) Then Directory.CreateDirectory(dir)
        Return dir
    End Function

    Private Sub MigrateLegacyLicenseFileIfNeeded(fileName As String, newPath As String)
        Try
            If File.Exists(newPath) Then Return
            Dim legacyPath = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "License", fileName)
            If File.Exists(legacyPath) Then File.Copy(legacyPath, newPath)
        Catch
            ' Best-effort -- a failed migration just means re-activation is needed,
            ' not a crash on launch.
        End Try
    End Sub

    ' =====================================================
    ' MACHINE CODE
    ' =====================================================

    ' Salts the fingerprint so this tool's code never coincides with another
    ' Adroitec tool's code on the same machine, even though the underlying
    ' hardware fields are identical. Change per tool.
    Private Const ToolName As String = "BulkPDFPublisher"
    Private Const SerialAlphabet As String = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"

    Public Function GenerateLicenseCode() As String
        ' Same algorithm as the other Adroitec tools: hash the raw WMI
        ' fingerprint (+ ComputerSystemProduct.UUID, included because the other
        ' four fields are model/firmware strings that repeat across identical
        ' hardware batches) and encode it into a readable dashed serial, rather
        ' than exposing the raw WMI values directly.
        Dim raw = ToolName & "|" & GetSystemInfo()
        Dim hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw))
        Dim serial = EncodeToSerial(hashBytes, 20)
        Return String.Join("-", SplitInChunks(serial, 5))
    End Function

    Private Function EncodeToSerial(hash As Byte(), length As Integer) As String
        Dim sb As New StringBuilder()
        For i = 0 To length - 1
            Dim b = hash(i Mod hash.Length)
            Dim idx = (b + i * 7) Mod SerialAlphabet.Length
            sb.Append(SerialAlphabet(idx))
        Next
        Return sb.ToString()
    End Function

    Private Iterator Function SplitInChunks(s As String, chunkSize As Integer) As IEnumerable(Of String)
        For i = 0 To s.Length - 1 Step chunkSize
            Yield s.Substring(i, Math.Min(chunkSize, s.Length - i))
        Next
    End Function

    Private Function GetSystemInfo() As String
        Dim info = ""
        info &= GetWMI("Win32_Processor", "ProcessorId")
        info &= GetWMI("Win32_BaseBoard", "Product")
        info &= GetWMI("Win32_DiskDrive", "Signature")
        info &= GetWMI("Win32_BIOS", "Version")
        info &= GetWMI("Win32_ComputerSystemProduct", "UUID")
        Return New String(info.Where(Function(c) Char.IsLetterOrDigit(c)).ToArray())
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
    ' FILE IO
    ' =====================================================
    Private Function ReadLicense(plainBytes As Byte()) As Dictionary(Of String, String)
        Dim dict As New Dictionary(Of String, String)
        Dim text = Encoding.UTF8.GetString(plainBytes)
        For Each line In text.Split({vbCrLf, vbLf}, StringSplitOptions.None)
            Dim idx = line.IndexOf(":"c)
            If idx > 0 Then dict(line.Substring(0, idx).Trim()) = line.Substring(idx + 1).Trim()
        Next
        Return dict
    End Function

    Private Function SerializeLicense(data As Dictionary(Of String, String)) As String
        Dim sb As New StringBuilder()
        For Each kv In data
            sb.Append($"{kv.Key}:{kv.Value}{vbLf}")
        Next
        Return sb.ToString()
    End Function

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
    ' CRYPTO
    ' =====================================================
    Private Function DecryptFile(path As String) As Byte()
        Dim keyIv = LicKeyIv()
        Using aes As Aes = Aes.Create()
            aes.Key = keyIv.Key
            aes.IV = keyIv.IV
            Using ms As New MemoryStream()
                Using fs As New FileStream(path, FileMode.Open, FileAccess.Read)
                    Using cs As New CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write)
                        fs.CopyTo(cs)
                    End Using
                End Using
                Return ms.ToArray()
            End Using
        End Using
    End Function

    Private Sub EncryptToFile(plainText As String, path As String)
        Dim keyIv = LicKeyIv()
        Using aes As Aes = Aes.Create()
            aes.Key = keyIv.Key
            aes.IV = keyIv.IV
            Using fsOut As New FileStream(path, FileMode.Create, FileAccess.Write)
                Using cs As New CryptoStream(fsOut, aes.CreateEncryptor(), CryptoStreamMode.Write)
                    Dim bytes = Encoding.UTF8.GetBytes(plainText)
                    cs.Write(bytes, 0, bytes.Length)
                End Using
            End Using
        End Using
    End Sub

    Private Function LicKeyIv() As (Key As Byte(), IV As Byte())
        Dim hash = SHA512.HashData(Encoding.ASCII.GetBytes(LicfilePwd)) ' matches PHP sha512() on the same ASCII password
        Return (hash.Take(32).ToArray(), hash.Skip(32).Take(16).ToArray())
    End Function

End Module
