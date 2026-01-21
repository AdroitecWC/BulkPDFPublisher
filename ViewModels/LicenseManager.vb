Imports System.IO
Imports System.Text
Imports System.Security.Cryptography
Imports Microsoft.Win32
Imports System.Management

Public Module LicenseManager

    Private Const LicfilePwd As String = "Kbe@Adr"
    Private Const LicenseFilePath As String = "Resources\License\License.lic"
    Private Const RegPath As String = "Software\MyCompany\MyTool"
    Private Const RevokedRegPath As String = RegPath & "\RevokedLicenses"

    ' =====================================================
    ' Install License (HARDENED)
    ' =====================================================
    Public Function InstallLicense(sourceLicPath As String) As Boolean
        Dim tempPath As String = Path.GetTempPath() & "TempInstallCheck.txt"

        Try
            ' 1. Decrypt license to temp
            Dim key = CreateKey(LicfilePwd)
            Dim iv = CreateIV(LicfilePwd)

            EncryptOrDecryptFile(
            sourceLicPath,
            tempPath,
            key,
            iv,
            CryptoAction.ActionDecrypt
        )

            ' 2. Read LicenseId (if present)
            Dim licenseId As String = ""

            Using reader As New StreamReader(tempPath)
                Dim firstLine = reader.ReadLine()
                If firstLine IsNot Nothing AndAlso
               firstLine.StartsWith("LicenseId:", StringComparison.OrdinalIgnoreCase) Then
                    licenseId = firstLine.Replace("LicenseId:", "").Trim()
                End If
            End Using

            ' 3. BLOCK revoked licenses
            If Not String.IsNullOrEmpty(licenseId) AndAlso IsLicenseRevoked(licenseId) Then
                MessageBox.Show(
                "This license has already been transferred and is no longer valid.",
                "License Invalid",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            )
                Return False
            End If

            ' 4. Install license (safe)
            Dim destPath = GetLicenseFilePath()

            Directory.CreateDirectory(Path.GetDirectoryName(destPath))
            File.Copy(sourceLicPath, destPath, True)

            Registry.CurrentUser.CreateSubKey(RegPath).SetValue("Installed", "1")
            Return True

        Catch
            Return False

        Finally
            If File.Exists(tempPath) Then
                Try : File.Delete(tempPath) : Catch : End Try
            End If
        End Try
    End Function


    ' =====================================================
    ' Check License (HARDENED)
    ' =====================================================
    Public Function CheckLicense() As Boolean
        Dim fullPath = GetLicenseFilePath()

        Dim tempPath = Path.Combine(Path.GetTempPath(), "TempLicCheck.txt")

        If Not File.Exists(fullPath) Then Return False

        Dim key = CreateKey(LicfilePwd)
        Dim iv = CreateIV(LicfilePwd)

        Try
            EncryptOrDecryptFile(fullPath, tempPath, key, iv, CryptoAction.ActionDecrypt)
        Catch
            Return False
        End Try

        Try
            Using reader As New StreamReader(tempPath)

                ' ---- LicenseId (OPTIONAL for old licenses)
                Dim firstLine As String = reader.ReadLine()
                Dim licenseId As String = ""

                If firstLine.StartsWith("LicenseId:", StringComparison.OrdinalIgnoreCase) Then
                    licenseId = firstLine.Replace("LicenseId:", "").Trim()

                    ' ---- STEP 5.4: Block revoked license
                    If IsLicenseRevoked(licenseId) Then
                        Return False
                    End If

                    ' Read LastDate line
                    reader.ReadLine()
                End If

                ' ---- Validate system code
                Dim expectedCode = GenerateLicenseCode()

                For Each ch As Char In expectedCode
                    If reader.EndOfStream Then Return False

                    Dim valLine = reader.ReadLine()
                    Dim val As Integer
                    If Not Integer.TryParse(valLine, val) Then Return False
                    If Asc(ch) <> val Then Return False
                Next
            End Using

            Return True

        Catch
            Return False

        Finally
            If File.Exists(tempPath) Then
                Try : File.Delete(tempPath) : Catch : End Try
            End If
        End Try
    End Function
    Private Function GetLicenseFilePath() As String
        Dim basePath As String = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Adroitec Engineering Solutions",
        "PDF Publisher",
        "License"
    )

        Directory.CreateDirectory(basePath)

        Return Path.Combine(basePath, "License.lic")
    End Function

    ' =====================================================
    ' Transfer active license (HARDENED)
    ' =====================================================
    Public Function TransferLicense(newSystemCode As String, outputPath As String) As String

        If String.IsNullOrWhiteSpace(newSystemCode) Then
            Throw New ArgumentException("New system code cannot be empty.")
        End If

        If Not CheckLicense() Then
            Throw New InvalidOperationException("No active license to transfer.")
        End If

        Dim currentSystemCode As String = GenerateLicenseCode()
        If String.Equals(newSystemCode.Trim(), currentSystemCode, StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException("Cannot transfer license to the same system.")
        End If

        ' --- Revoke current license (Step 5.3)
        Dim currentLicenseId As String = GetActiveLicenseId()
        If Not String.IsNullOrEmpty(currentLicenseId) Then
            RevokeLicense(currentLicenseId)
        End If

        ' --- Read expiry
        Dim expiryDate As String = GetActiveLicenseExpiry()

        ' --- USE USER-SELECTED PATH
        Dim transferPath As String = outputPath

        ' --- Generate license at selected location
        LicenseGenerationHelper.GenerateEncryptedLicense(
        newSystemCode.Trim(),
        expiryDate,
        transferPath
    )

        ' --- Deactivate current system
        DeactivateLicense()

        Return transferPath

    End Function

    ' =====================================================
    ' License Revocation Helpers
    ' =====================================================
    Private Sub RevokeLicense(licenseId As String)
        Registry.CurrentUser.
            CreateSubKey(RevokedRegPath).
            SetValue(licenseId, "1")
    End Sub
    ' =====================================================
    ' Return License (BACKWARD COMPATIBILITY)
    ' =====================================================
    Public Function ReturnLicense() As Boolean
        Try
            DeactivateLicense()

            Try
                Registry.CurrentUser.DeleteSubKey(RegPath, False)
            Catch
            End Try

            Return True
        Catch
            Return False
        End Try
    End Function

    Private Function IsLicenseRevoked(licenseId As String) As Boolean
        If String.IsNullOrEmpty(licenseId) Then Return False
        Using key = Registry.CurrentUser.OpenSubKey(RevokedRegPath)
            If key Is Nothing Then Return False
            Return key.GetValue(licenseId) IsNot Nothing
        End Using
    End Function

    Private Function GetActiveLicenseId() As String
        Dim fullPath = GetLicenseFilePath()

        Dim tempPath = Path.GetTempPath() & "TempLicId.txt"

        Dim key = CreateKey(LicfilePwd)
        Dim iv = CreateIV(LicfilePwd)

        EncryptOrDecryptFile(fullPath, tempPath, key, iv, CryptoAction.ActionDecrypt)

        Try
            Using reader As New StreamReader(tempPath)
                Dim line = reader.ReadLine()
                If line IsNot Nothing AndAlso line.StartsWith("LicenseId:", StringComparison.OrdinalIgnoreCase) Then
                    Return line.Replace("LicenseId:", "").Trim()
                End If
            End Using
        Finally
            If File.Exists(tempPath) Then File.Delete(tempPath)
        End Try

        Return ""
    End Function

    ' =====================================================
    ' Deactivate
    ' =====================================================
    Public Sub DeactivateLicense()
        Dim licensePath As String = GetLicenseFilePath()

        If File.Exists(licensePath) Then
            File.Delete(licensePath)
        End If
    End Sub

    ' =====================================================
    ' System Code (UNCHANGED)
    ' =====================================================
    Public Function GenerateLicenseCode() As String
        Return GetSystemInfo()
    End Function

    Private Function GetSystemInfo() As String
        Dim info As String = ""
        info &= GetWMI("Win32_Processor", "ProcessorId")
        info &= GetWMI("Win32_BaseBoard", "Product")
        info &= GetWMI("Win32_OperatingSystem", "SerialNumber")
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

    ' =====================================================
    ' Expiry Reader (UNCHANGED)
    ' =====================================================
    Public Function GetActiveLicenseExpiry() As String
        Dim fullPath = GetLicenseFilePath()

        Dim tempPath = Path.GetTempPath() & "TempLicExpiry.txt"

        Dim key = CreateKey(LicfilePwd)
        Dim iv = CreateIV(LicfilePwd)

        EncryptOrDecryptFile(fullPath, tempPath, key, iv, CryptoAction.ActionDecrypt)

        Try
            Using reader As New StreamReader(tempPath)
                Dim line = reader.ReadLine()
                If line.StartsWith("LicenseId:", StringComparison.OrdinalIgnoreCase) Then
                    line = reader.ReadLine()
                End If
                Return line.Replace("LastDate:", "").Trim()
            End Using
        Finally
            If File.Exists(tempPath) Then File.Delete(tempPath)
        End Try
    End Function

    ' =====================================================
    ' Crypto (UNCHANGED)
    ' =====================================================
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

    Public Sub EncryptOrDecryptFile(inputPath As String, outputPath As String,
                                    key() As Byte, iv() As Byte, direction As CryptoAction)

        Using fsInput As New FileStream(inputPath, FileMode.Open, FileAccess.Read)
            Using fsOutput As New FileStream(outputPath, FileMode.Create, FileAccess.Write)

                Dim csp As New RijndaelManaged()
                Dim cs As CryptoStream =
                    If(direction = CryptoAction.ActionEncrypt,
                       New CryptoStream(fsOutput, csp.CreateEncryptor(key, iv), CryptoStreamMode.Write),
                       New CryptoStream(fsOutput, csp.CreateDecryptor(key, iv), CryptoStreamMode.Write))

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
