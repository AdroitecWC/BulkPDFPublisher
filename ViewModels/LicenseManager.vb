Imports System.IO
Imports IOPath = System.IO.Path
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Security.Cryptography
Imports System.Timers
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
        NoSeatsAvailable    ' Floating: all seats checked out
        ServerUnreachable   ' Floating: cannot reach license server
        InvalidLicense      ' Floating: uid not registered on server
        WrongProduct        ' License is for a different product
    End Enum

    Private Const LicfilePwd As String = "Kbe@Adr"
    Private Const AppProduct As String = "Bulk PDF publish"
    Private Const UsedLicensesRegPath As String = "Software\Adroitec Engineering Solutions Pvt Ltd\MyTool\UsedLicenses"
    Private Const SettingsRegPath As String = "Software\Adroitec Engineering Solutions Pvt Ltd\MyTool\Settings"

    ' ── Floating license runtime state ───────────────────────────────────────
    Private _seatToken As String = ""
    Private _serverUrl As String = ""
    Private _heartbeatTimer As Timer = Nothing
    Private ReadOnly _http As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(7)}

    ' Filled after a NoSeatsAvailable response so the caller can show specifics
    Public LastSeatsInUse As Integer = 0
    Public LastSeatsMax As Integer = 0

    ' =====================================================
    ' PUBLIC API
    ' =====================================================

    ''' <summary>True if the license is valid and (for floating) a seat was obtained.</summary>
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

            ' Expiry check — same for both node-locked and floating
            If data.ContainsKey("LastDate") AndAlso data("LastDate") <> "Nil" Then
                Dim exp As DateTime
                If DateTime.TryParse(data("LastDate"), exp) AndAlso exp < DateTime.Today Then
                    Return LicenseError.Expired
                End If
            End If

            ' ── FLOATING LICENSE ─────────────────────────────────────────────
            If data.ContainsKey("LicenseType") AndAlso data("LicenseType") = "Floating" Then
                Dim licProduct = If(data.ContainsKey("Product"), data("Product"), "")
                If Not licProduct.StartsWith(AppProduct, StringComparison.OrdinalIgnoreCase) Then
                    Return LicenseError.WrongProduct
                End If

                Dim uid = If(data.ContainsKey("LicenseUid"), data("LicenseUid"), "")
                ' A manually entered + verified address (Network License Server field) takes
                ' precedence over whatever ServerUrl is baked into the .lic file, since that's
                ' exactly the point of letting an admin repoint a client without reissuing a license.
                _serverUrl = If(GetServerUrlOverride(), If(data.ContainsKey("ServerUrl"), data("ServerUrl").TrimEnd("/"c), ""))
                If uid = "" OrElse _serverUrl = "" Then Return LicenseError.LicenseTampered

                Return FloatingCheckoutDetailed(uid, GenerateLicenseCode())
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
    ' ACTIVATE LICENSE (FIRST TIME — node-locked only)
    ' =====================================================
    Public Function ActivateLicense() As Boolean
        Dim fullPath = GetLicensePath()
        If Not File.Exists(fullPath) Then Return False

        Try
            Dim data = ReadLicense(DecryptFile(fullPath))

            ' Floating licenses are pre-activated by the server
            If data.ContainsKey("LicenseType") AndAlso data("LicenseType") = "Floating" Then
                Return True
            End If

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

            If data.ContainsKey("LicenseType") AndAlso data("LicenseType") = "Floating" Then
                FloatingRelease()
            End If

            data("Activated") = "False"
            EncryptToFile(SerializeLicense(data), fullPath)
            Return True

        Catch
            Return False
        End Try
    End Function

    ' =====================================================
    ' FLOATING — CHECKOUT / RELEASE / HEARTBEAT
    ' =====================================================
    Private Function FloatingCheckoutDetailed(uid As String, machineCode As String) As LicenseError
        Try
            Dim machineName = Environment.MachineName
            Dim form As New Dictionary(Of String, String) From {
                {"uid", uid},
                {"machine", machineCode},
                {"machine_name", machineName}
            }

            Dim json As String
            Try
                Dim resp = _http.PostAsync($"{_serverUrl}/checkout.php", New FormUrlEncodedContent(form)).GetAwaiter().GetResult()
                json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            Catch
                Return LicenseError.ServerUnreachable
            End Try

            ' If the response isn't JSON (e.g. HTML 404 from a wrong port/URL), treat as unreachable
            If Not json.TrimStart().StartsWith("{") Then Return LicenseError.ServerUnreachable

            Using doc = JsonDocument.Parse(json)
                Dim root = doc.RootElement

                Dim successEl As JsonElement
                Dim success = root.TryGetProperty("success", successEl) AndAlso successEl.ValueKind = JsonValueKind.True

                If success Then
                    Dim tokenEl As JsonElement
                    If root.TryGetProperty("seat_token", tokenEl) Then
                        _seatToken = tokenEl.GetString()

                        Dim uEl As JsonElement
                        Dim mEl As JsonElement
                        LastSeatsInUse = If(root.TryGetProperty("in_use", uEl), uEl.GetInt32(), 0)
                        LastSeatsMax = If(root.TryGetProperty("max_seats", mEl), mEl.GetInt32(), 0)

                        StartHeartbeat()
                        Return LicenseError.None
                    End If
                End If

                Dim uFailEl As JsonElement
                Dim mFailEl As JsonElement
                LastSeatsInUse = If(root.TryGetProperty("in_use", uFailEl), uFailEl.GetInt32(), 0)
                LastSeatsMax = If(root.TryGetProperty("max_seats", mFailEl), mFailEl.GetInt32(), 0)

                Dim reasonEl As JsonElement
                Dim reason = If(root.TryGetProperty("reason", reasonEl), reasonEl.GetString(), "")
                If reason = "no_seats" Then Return LicenseError.NoSeatsAvailable
                If reason = "invalid_license" Then Return LicenseError.InvalidLicense
                Return LicenseError.NoSeatsAvailable
            End Using

        Catch
            Return LicenseError.ServerUnreachable
        End Try
    End Function

    Public Sub FloatingRelease()
        If _seatToken = "" OrElse _serverUrl = "" Then Return
        StopHeartbeat()
        Try
            Dim form As New Dictionary(Of String, String) From {{"seat_token", _seatToken}}
            _http.PostAsync($"{_serverUrl}/release.php", New FormUrlEncodedContent(form)).GetAwaiter().GetResult()
        Catch
        Finally
            _seatToken = ""
        End Try
    End Sub

    Private Sub StartHeartbeat()
        StopHeartbeat()
        _heartbeatTimer = New Timer(180000)  ' every 3 minutes
        AddHandler _heartbeatTimer.Elapsed, AddressOf OnHeartbeat
        _heartbeatTimer.AutoReset = True
        _heartbeatTimer.Start()
    End Sub

    Private Sub OnHeartbeat(sender As Object, e As ElapsedEventArgs)
        If _seatToken = "" OrElse _serverUrl = "" Then Return
        Try
            Dim form As New Dictionary(Of String, String) From {{"seat_token", _seatToken}}
            _http.PostAsync($"{_serverUrl}/heartbeat.php", New FormUrlEncodedContent(form)).GetAwaiter().GetResult()
        Catch
        End Try
    End Sub

    Private Sub StopHeartbeat()
        If _heartbeatTimer IsNot Nothing Then
            _heartbeatTimer.Stop()
            _heartbeatTimer.Dispose()
            _heartbeatTimer = Nothing
        End If
    End Sub

    Public Function IsFloatingLicense() As Boolean
        Dim licPath = GetLicensePath()
        If Not File.Exists(licPath) Then Return False
        Try
            Dim data = ReadLicense(DecryptFile(licPath))
            Return data.ContainsKey("LicenseType") AndAlso data("LicenseType") = "Floating"
        Catch
            Return False
        End Try
    End Function

    ' =====================================================
    ' LICENSE PATH
    ' =====================================================
    Public Function GetLicensePath() As String
        Dim path = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "License", "License.lic")
        Dim dir = IOPath.GetDirectoryName(path)
        If Not Directory.Exists(dir) Then Directory.CreateDirectory(dir)
        Return path
    End Function

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
    ' NETWORK LICENSE SERVER — manual address entry + connectivity test
    ' =====================================================

    ''' <summary>
    ''' Parses a "host", "host:port", or "host@port" address (matching the Server
    ''' Name shown on the FloatingLicenseServer app), probes its checkout endpoint,
    ''' and — only if a server actually answers — saves it so future floating
    ''' checkouts use this address instead of the one baked into the .lic file.
    ''' </summary>
    Public Function TestAndSaveServerUrl(rawAddress As String, ByRef message As String) As Boolean
        Dim url = BuildServerUrl(rawAddress)
        If url Is Nothing Then
            message = "Enter a server address, e.g. vizserver:1122 or vizserver@1122."
            Return False
        End If

        Try
            Dim form As New Dictionary(Of String, String) From {
                {"uid", "__connectivity_test__"}, {"machine", "connectivity-test"}, {"machine_name", "connectivity-test"}
            }
            Dim resp = _http.PostAsync($"{url}/checkout.php", New FormUrlEncodedContent(form)).GetAwaiter().GetResult()
            Dim json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            If Not json.TrimStart().StartsWith("{") Then
                message = $"No license server responded at {url}."
                Return False
            End If
        Catch
            message = $"Could not reach a license server at {url}." & vbCrLf & "Check the address and try again."
            Return False
        End Try

        SaveServerUrlOverride(url)
        message = $"Connected to the license server at {url} successfully."
        Return True
    End Function

    Private Function BuildServerUrl(rawAddress As String) As String
        Dim addr = If(rawAddress, "").Trim()
        If addr = "" Then Return Nothing

        Dim host As String
        Dim port As Integer = 80
        If addr.Contains("@"c) OrElse addr.Contains(":"c) Then
            Dim sep = If(addr.Contains("@"c), "@"c, ":"c)
            Dim parts = addr.Split({sep}, 2)
            host = parts(0).Trim()
            If Not Integer.TryParse(parts(1).Trim(), port) Then port = 80
        Else
            host = addr
        End If
        If host = "" Then Return Nothing

        Return If(port = 80, $"http://{host}/web/api/floating", $"http://{host}:{port}/web/api/floating")
    End Function

    Private Sub SaveServerUrlOverride(url As String)
        Using k = Registry.CurrentUser.CreateSubKey(SettingsRegPath)
            k.SetValue("FloatingServerOverride", url)
        End Using
    End Sub

    Private Function GetServerUrlOverride() As String
        Using k = Registry.CurrentUser.OpenSubKey(SettingsRegPath)
            Dim v = If(k IsNot Nothing, TryCast(k.GetValue("FloatingServerOverride"), String), Nothing)
            Return If(String.IsNullOrWhiteSpace(v), Nothing, v)
        End Using
    End Function

    ' =====================================================
    ' REGISTRY
    ' =====================================================
    Private Function IsLicenseUsed(id As String) As Boolean
        Using k = Registry.CurrentUser.OpenSubKey(UsedLicensesRegPath)
            Return k IsNot Nothing AndAlso k.GetValue(id) IsNot Nothing
        End Using
    End Function

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
