Option Explicit On
Option Strict On

Imports System.IO
Imports System.Security.Cryptography
Imports System.Text

Public Module LicenseGenerationHelper

#Region "Constants"

    ' MUST remain same across generator & app
    Public Const LicfilePwd As String = "Kbe@Adr"

#End Region

#Region "Public API"

    ' =====================================================
    ' Generate encrypted license file
    ' =====================================================
    Public Sub GenerateEncryptedLicense(
        requestCode As String,
        expiryDate As String,
        outputLicensePath As String
    )

        Dim plainText As String =
            GeneratePlainLicenseContent(requestCode, expiryDate)

        Dim tempPlainPath As String = Path.GetTempFileName()

        Try
            File.WriteAllText(tempPlainPath, plainText, Encoding.ASCII)

            Dim key As Byte() = CreateKey(LicfilePwd)
            Dim iv As Byte() = CreateIV(LicfilePwd)

            EncryptOrDecryptFile(
                tempPlainPath,
                outputLicensePath,
                key,
                iv,
                CryptoAction.ActionEncrypt
            )

        Finally
            If File.Exists(tempPlainPath) Then
                File.Delete(tempPlainPath)
            End If
        End Try

    End Sub

#End Region

#Region "Plain License Content"

    ' =====================================================
    ' Generates plain license content
    ' FORMAT (order matters):
    ' LicenseId:<GUID>
    ' LastDate:<expiry>
    ' ASCII values of request code
    ' =====================================================
    Private Function GeneratePlainLicenseContent(
        requestCode As String,
        expiryDate As String
    ) As String

        Dim sb As New StringBuilder()

        ' Unique License ID (used for revocation)
        Dim licenseId As String = Guid.NewGuid().ToString()

        sb.AppendLine("LicenseId:" & licenseId)
        sb.AppendLine("LastDate:" & expiryDate)

        For Each ch As Char In requestCode
            sb.AppendLine(Asc(ch).ToString())
        Next

        Return sb.ToString()

    End Function

#End Region

#Region "Key & IV Generation (UNCHANGED)"

    Public Function CreateKey(strPassword As String) As Byte()

        Dim chrData() As Char = strPassword.ToCharArray()
        Dim bytDataToHash(chrData.Length - 1) As Byte

        For i As Integer = 0 To chrData.Length - 1
            bytDataToHash(i) = CByte(Asc(chrData(i)))
        Next

        Dim sha512 As New SHA512Managed()
        Dim bytResult As Byte() = sha512.ComputeHash(bytDataToHash)

        Dim bytKey(31) As Byte
        Array.Copy(bytResult, bytKey, 32)

        Return bytKey

    End Function

    Public Function CreateIV(strPassword As String) As Byte()

        Dim chrData() As Char = strPassword.ToCharArray()
        Dim bytDataToHash(chrData.Length - 1) As Byte

        For i As Integer = 0 To chrData.Length - 1
            bytDataToHash(i) = CByte(Asc(chrData(i)))
        Next

        Dim sha512 As New SHA512Managed()
        Dim bytResult As Byte() = sha512.ComputeHash(bytDataToHash)

        Dim bytIV(15) As Byte
        Array.Copy(bytResult, 32, bytIV, 0, 16)

        Return bytIV

    End Function

#End Region

#Region "Encrypt / Decrypt File (UNCHANGED)"

    Public Enum CryptoAction
        ActionEncrypt = 1
        ActionDecrypt = 2
    End Enum

    Public Sub EncryptOrDecryptFile(
        strInputFile As String,
        strOutputFile As String,
        bytKey() As Byte,
        bytIV() As Byte,
        Direction As CryptoAction
    )

        Dim fsInput As FileStream = Nothing
        Dim fsOutput As FileStream = Nothing
        Dim csCryptoStream As CryptoStream = Nothing

        Try
            fsInput = New FileStream(strInputFile, FileMode.Open, FileAccess.Read)
            fsOutput = New FileStream(strOutputFile, FileMode.Create, FileAccess.Write)

            Dim rijndael As New RijndaelManaged()

            If Direction = CryptoAction.ActionEncrypt Then
                csCryptoStream = New CryptoStream(
                    fsOutput,
                    rijndael.CreateEncryptor(bytKey, bytIV),
                    CryptoStreamMode.Write
                )
            Else
                csCryptoStream = New CryptoStream(
                    fsOutput,
                    rijndael.CreateDecryptor(bytKey, bytIV),
                    CryptoStreamMode.Write
                )
            End If

            Dim buffer(4096) As Byte
            Dim bytesRead As Integer

            Do
                bytesRead = fsInput.Read(buffer, 0, buffer.Length)
                If bytesRead > 0 Then
                    csCryptoStream.Write(buffer, 0, bytesRead)
                End If
            Loop While bytesRead > 0

        Finally
            If csCryptoStream IsNot Nothing Then csCryptoStream.Close()
            If fsInput IsNot Nothing Then fsInput.Close()
            If fsOutput IsNot Nothing Then fsOutput.Close()
        End Try

    End Sub

#End Region

End Module
