Imports System.IO
Imports IOPath = System.IO.Path
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Security.Cryptography
Imports System.Timers
Imports Microsoft.Win32
Imports System.Management
Imports System.Globalization

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
        InvalidLicense      ' Floating: uid/code not registered on server
        WrongProduct        ' License is for a different product
        ProductNotLicensed  ' Floating: Activation Code is valid, but no tranche is loaded for this product yet
        AmbiguousCustomer   ' Floating: Activation Code matched tranches for more than one customer
    End Enum

    Private Const LicfilePwd As String = "Kbe@Adr"
    Public Const AppProduct As String = "Bulk PDF publish"
    ' Scoped by ToolName -- these paths used to be the literal, identical string across
    ' every Adroitec tool ("...\MyTool\..."), so activating one tool with a License Key
    ' would overwrite the same registry value another tool reads back, making it look
    ' like "every tool accepts the same key". Each tool now gets its own subkey.
    Private Const UsedLicensesRegPath As String = "Software\Adroitec Engineering Solutions Pvt Ltd\BulkPDFPublisher\UsedLicenses"
    Private Const SettingsRegPath As String = "Software\Adroitec Engineering Solutions Pvt Ltd\BulkPDFPublisher\Settings"

    ' ── Floating license runtime state ───────────────────────────────────────
    Private _seatToken As String = ""
    Private _serverUrl As String = ""
    Private _heartbeatTimer As Timer = Nothing
    Private ReadOnly _http As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(7)}

    ' Filled after a NoSeatsAvailable response so the caller can show specifics
    Public LastSeatsInUse As Integer = 0
    Public LastSeatsMax As Integer = 0

    ' Filled by ActivateWithKey so the caller can tell "server unreachable" apart
    ' from other failures without parsing the message string.
    Public LastActivationError As LicenseError = LicenseError.None

    ' Same idea as LastActivationError, but for BorrowSeat.
    Public LastBorrowError As LicenseError = LicenseError.None

    ''' <summary>The server URL the last checkout attempt actually used -- lets a
    ''' "server not available" dialog show what was tried instead of leaving the user
    ''' to guess whether the address they typed even parsed correctly.</summary>
    Public ReadOnly Property LastServerUrl As String
        Get
            Return _serverUrl
        End Get
    End Property

    ' =====================================================
    ' PUBLIC API
    ' =====================================================

    ''' <summary>True if the license is valid and (for floating) a seat was obtained.</summary>
    Public Function CheckLicense() As Boolean
        Return CheckLicenseDetailed() = LicenseError.None
    End Function

    ''' <summary>Returns the specific reason for failure, or None on success.</summary>
    Public Function CheckLicenseDetailed() As LicenseError
        ' A live borrow is checked first and needs no network call at all -- this is
        ' what lets the app run fully offline for the borrow window. See TryGetActiveBorrow.
        Dim borrowExpiry As DateTime
        If TryGetActiveBorrow(borrowExpiry) Then Return LicenseError.None

        ' A key-based activation (License Key field, no .lic file at all) takes
        ' priority over a local file every time — once someone's activated with a
        ' key, that's the intended source of truth for this install.
        Dim activatedUid = GetActivatedUid()
        If activatedUid IsNot Nothing Then
            _serverUrl = If(GetServerUrlOverride(), "")
            If _serverUrl = "" Then Return LicenseError.LicenseTampered
            Return FloatingCheckoutDetailed(activatedUid, GenerateLicenseCode(), isActivationCode:=True)
        End If

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

            ' ── FLOATING LICENSE ─────────────────────────────────────────────
            ' Checked before the local LastDate gate below on purpose: a floating
            ' tranche's expiry lives on the NLM's live pool and can be renewed there
            ' without this client ever re-downloading a new .lic, so the date baked
            ' into whichever file happens to be loaded is not authoritative -- let
            ' FloatingCheckoutDetailed's server round-trip decide instead (it maps the
            ' server's own "license_expired" reason to LicenseError.Expired).
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

            ' Expiry check — node-locked only now (see comment above for floating)
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
        ' Activation-Code-based floating activation has no .lic file at all -- release
        ' the checked-out seat and forget the saved code so the next launch requires
        ' reactivation, instead of falling through to the file-based path below and
        ' reporting "no active license found" for an install that IS actually active.
        Dim activatedUid = GetActivatedUid()
        Dim didSomething = False
        If activatedUid IsNot Nothing Then
            FloatingRelease()
            ClearActivatedUid()
            didSomething = True
        End If

        ' Always also clear any file-based activation, even when an Activation Code was
        ' the live path above -- a stale .lic file left next to the exe from an earlier
        ' attempt (still marked Activated=True) must not resurface as a bogus license on
        ' the next launch once the code is cleared and CheckLicenseDetailed falls through
        ' to the file-based path. Only release a floating seat for it if the file path was
        ' actually the one in use (activatedUid was Nothing) -- otherwise that seat was
        ' already released above.
        Dim fullPath = GetLicensePath()
        If File.Exists(fullPath) Then
            Try
                Dim data = ReadLicense(DecryptFile(fullPath))

                If activatedUid Is Nothing AndAlso data.ContainsKey("LicenseType") AndAlso data("LicenseType") = "Floating" Then
                    FloatingRelease()
                End If

                data("Activated") = "False"
                EncryptToFile(SerializeLicense(data), fullPath)
                didSomething = True

            Catch
                ' Corrupt/unreadable file -- leave didSomething as-is from the code branch.
            End Try
        End If

        Return didSomething
    End Function

    ' =====================================================
    ' CHANGE LICENSE TYPE (switch node-locked <-> floating)
    ' =====================================================

    ''' <summary>Best-effort cleanup of whatever activation currently exists (borrow,
    ''' activation-code floating, or file-based license), so a "Change License" action
    ''' always leaves this install clean and ready for LicenseWindow to activate a
    ''' different license on the next launch. Unlike DeactivateLicense(), this succeeds
    ''' even if nothing was active yet -- the point is guaranteed clean state, not
    ''' reporting whether there was anything to clean.</summary>
    Public Function PrepareForLicenseTypeSwitch() As Boolean
        Dim expiresLocal As DateTime
        If TryGetActiveBorrow(expiresLocal) Then
            Dim msg As String = Nothing
            ReturnBorrowedSeat(msg)
        End If

        Dim activatedUid = GetActivatedUid()
        If activatedUid IsNot Nothing Then
            FloatingRelease()
            ClearActivatedUid()
        End If

        ' Always also clear any file-based activation, even when an Activation Code was
        ' the live path above -- a stale .lic file left next to the exe from an earlier
        ' attempt (still marked Activated=True, possibly with an old/expired LastDate)
        ' must not resurface (as LicenseError.Expired etc.) once the code is cleared and
        ' CheckLicenseDetailed falls through to the file-based path on the next launch.
        ' Only release a floating seat for it if the file path was actually the one in
        ' use (activatedUid was Nothing) -- otherwise that seat was already released above.
        Dim fullPath = GetLicensePath()
        If File.Exists(fullPath) Then
            Try
                Dim data = ReadLicense(DecryptFile(fullPath))

                If activatedUid Is Nothing AndAlso data.ContainsKey("LicenseType") AndAlso data("LicenseType") = "Floating" Then
                    FloatingRelease()
                End If

                data("Activated") = "False"
                EncryptToFile(SerializeLicense(data), fullPath)
            Catch
                ' A corrupt/unreadable file shouldn't block switching -- LicenseWindow
                ' will just treat it as absent/invalid on the next launch.
            End Try
        End If

        ' Deliberately NOT clearing FloatingServerOverride here -- it's just the last
        ' connection endpoint, not tied to which license type is active, and wiping it
        ' forced a blind retype of the server IP:port on every Change License (the
        ' Network License Server field otherwise starts blank) where any typo/blank
        ' entry then looked exactly like the server being unreachable. LicenseWindow
        ' now pre-fills from it via GetLastServerAddress(); the user can still type a
        ' different address to point at a different server.
        Return True
    End Function

    ' =====================================================
    ' TRANSFER LICENSE (node-locked only, one-time)
    ' =====================================================
    Public Function TransferLicense(newSystemCode As String, savePath As String) As Boolean
        Dim fullPath = GetLicensePath()
        If Not File.Exists(fullPath) Then Return False

        Try
            Dim data = ReadLicense(DecryptFile(fullPath))

            ' Floating licenses cannot be machine-transferred
            If data.ContainsKey("LicenseType") AndAlso data("LicenseType") = "Floating" Then
                Return False
            End If

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
    ' FLOATING — CHECKOUT / RELEASE / HEARTBEAT
    ' =====================================================
    Private Function FloatingCheckoutDetailed(uid As String, machineCode As String, Optional isActivationCode As Boolean = False) As LicenseError
        Try
            Dim machineName = Environment.MachineName
            Dim form As New Dictionary(Of String, String) From {
                {"machine", machineCode},
                {"machine_name", machineName},
                {"product", AppProduct}
            }
            ' A file-based/legacy per-product License Key is sent as "uid" (exact-match
            ' lookup on the server); the shared, admin-set Activation Code is sent as
            ' "code" (resolved by product name instead) -- see LicenseServer.Checkout().
            form(If(isActivationCode, "code", "uid")) = uid

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
                If reason = "wrong_product" Then Return LicenseError.WrongProduct
                If reason = "product_not_licensed" Then Return LicenseError.ProductNotLicensed
                If reason = "ambiguous_customer" Then Return LicenseError.AmbiguousCustomer
                If reason = "license_expired" Then Return LicenseError.Expired
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

    ' =====================================================
    ' FLOATING — BORROW / RETURN (offline use for N days)
    ' =====================================================

    ''' <summary>
    ''' Resolves the currently-active floating source (Activation Code or file-based
    ''' Floating license) the same way CheckLicenseDetailed()'s floating branches do,
    ''' for BorrowSeat() to reuse. Kept as a read-only duplication rather than a shared
    ''' refactor of CheckLicenseDetailed() itself, to avoid touching that already-audited
    ''' path -- if the resolution rules there ever change, mirror the change here too.
    ''' </summary>
    Private Function ResolveFloatingSource(ByRef uid As String, ByRef isActivationCode As Boolean, ByRef url As String) As Boolean
        Dim activatedUid = GetActivatedUid()
        If activatedUid IsNot Nothing Then
            Dim ov = GetServerUrlOverride()
            If String.IsNullOrEmpty(ov) Then Return False
            uid = activatedUid
            isActivationCode = True
            url = ov
            Return True
        End If

        Dim fullPath = GetLicensePath()
        If Not File.Exists(fullPath) Then Return False
        Try
            Dim data = ReadLicense(DecryptFile(fullPath))
            If Not data.ContainsKey("LicenseType") OrElse data("LicenseType") <> "Floating" Then Return False

            Dim licUid = If(data.ContainsKey("LicenseUid"), data("LicenseUid"), "")
            Dim licUrl = If(GetServerUrlOverride(), If(data.ContainsKey("ServerUrl"), data("ServerUrl").TrimEnd("/"c), ""))
            If licUid = "" OrElse licUrl = "" Then Return False

            uid = licUid
            isActivationCode = False
            url = licUrl
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Checks out a seat for N hours of fully offline use (the server clamps
    ''' this to both its own max-borrow-window and the license's own expiry -- see
    ''' LicenseServer.Borrow()). On success, stops any live heartbeat (the server already
    ''' converted or would reject a duplicate live session) and writes an encrypted,
    ''' machine-bound Borrow.lic — see TryGetActiveBorrow for how it's read back with no
    ''' network call.</summary>
    Public Function BorrowSeat(hours As Double, ByRef message As String) As Boolean
        LastBorrowError = LicenseError.None

        Dim uid As String = Nothing
        Dim isActivationCode As Boolean
        Dim url As String = Nothing
        If Not ResolveFloatingSource(uid, isActivationCode, url) Then
            message = "Activate a floating license first."
            Return False
        End If
        _serverUrl = url

        Try
            Dim form As New Dictionary(Of String, String) From {
                {"machine", GenerateLicenseCode()},
                {"machine_name", Environment.MachineName},
                {"product", AppProduct},
                {"hours", hours.ToString(Globalization.CultureInfo.InvariantCulture)}
            }
            form(If(isActivationCode, "code", "uid")) = uid

            Dim json As String
            Try
                Dim resp = _http.PostAsync($"{url}/borrow.php", New FormUrlEncodedContent(form)).GetAwaiter().GetResult()
                json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            Catch
                LastBorrowError = LicenseError.ServerUnreachable
                message = $"Could not reach a license server at {url}."
                Return False
            End Try

            If Not json.TrimStart().StartsWith("{") Then
                LastBorrowError = LicenseError.ServerUnreachable
                message = $"No license server responded at {url}."
                Return False
            End If

            Using doc = JsonDocument.Parse(json)
                Dim root = doc.RootElement
                Dim successEl As JsonElement
                Dim success = root.TryGetProperty("success", successEl) AndAlso successEl.ValueKind = JsonValueKind.True

                Dim tokenEl As JsonElement
                Dim expEl As JsonElement
                If success AndAlso root.TryGetProperty("borrow_token", tokenEl) AndAlso root.TryGetProperty("expires_utc", expEl) Then
                    ' The server is authoritative on the borrow window -- stored verbatim,
                    ' never computed locally, so a skewed client clock can't extend it.
                    Dim expiresUtc As DateTime
                    If DateTime.TryParse(expEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, expiresUtc) Then
                        StopHeartbeat()
                        _seatToken = ""
                        WriteBorrowLicense(uid, isActivationCode, tokenEl.GetString(), expiresUtc)

                        ' The server silently shortens the requested period if it would run
                        ' past this license's own expiry, or past its own max-borrow-window --
                        ' say so explicitly instead of just handing back a shorter date with
                        ' no explanation for why it doesn't match what was typed.
                        Dim clampedReasonEl As JsonElement
                        Dim clampedReason = If(root.TryGetProperty("clamped_reason", clampedReasonEl), clampedReasonEl.GetString(), "")
                        Dim clampNote = ""
                        Select Case clampedReason
                            Case "license_expiry"
                                clampNote = " (shortened -- this license expires before the period you requested.)"
                            Case "max_window"
                                clampNote = " (shortened -- borrowing is capped at the maximum allowed window.)"
                        End Select
                        message = $"Borrowed for offline use until {expiresUtc.ToLocalTime():yyyy-MM-dd HH:mm}.{clampNote}"
                        Return True
                    End If
                End If

                Dim reasonEl As JsonElement
                Dim reason = If(root.TryGetProperty("reason", reasonEl), reasonEl.GetString(), "")
                Select Case reason
                    Case "no_seats"
                        LastBorrowError = LicenseError.NoSeatsAvailable
                        message = "No seats available to borrow right now."
                    Case "invalid_license"
                        LastBorrowError = LicenseError.InvalidLicense
                        message = "The server does not recognise this license."
                    Case "wrong_product"
                        LastBorrowError = LicenseError.WrongProduct
                        message = "This license was issued for a different Adroitec product."
                    Case "product_not_licensed"
                        LastBorrowError = LicenseError.ProductNotLicensed
                        message = "This server doesn't have a license for this product loaded yet."
                    Case "ambiguous_customer"
                        LastBorrowError = LicenseError.AmbiguousCustomer
                        message = "This server has licenses loaded for more than one customer."
                    Case "license_expired"
                        LastBorrowError = LicenseError.Expired
                        message = "This license has expired."
                    Case Else
                        LastBorrowError = LicenseError.NoSeatsAvailable
                        message = "Could not borrow a seat."
                End Select
                Return False
            End Using

        Catch ex As Exception
            LastBorrowError = LicenseError.ServerUnreachable
            message = $"Borrow failed: {ex.Message}"
            Return False
        End Try
    End Function

    ''' <summary>True if Borrow.lic exists, is bound to this machine, and hasn't passed its
    ''' ExpiresUtc yet -- entirely local, no network call, so this works while offline.
    ''' A borrow past its expiry self-cleans here without needing to contact the server
    ''' (which independently frees the seat on its own schedule regardless).</summary>
    Public Function TryGetActiveBorrow(ByRef expiresLocal As DateTime) As Boolean
        expiresLocal = Nothing
        Dim path = GetBorrowLicensePath()
        If Not File.Exists(path) Then Return False

        Try
            Dim data = ReadLicense(DecryptFile(path))

            ' Bound to the machine that borrowed it -- a copied Borrow.lic must not
            ' grant offline access on a second PC.
            Dim machineId = GenerateLicenseCode()
            If Not data.ContainsKey("MachineId") OrElse data("MachineId") <> machineId Then Return False

            For i = 0 To machineId.Length - 1
                Dim key = $"C{i}"
                If Not data.ContainsKey(key) OrElse Asc(machineId(i)) <> Integer.Parse(data(key)) Then
                    Return False
                End If
            Next

            If Not data.ContainsKey("ExpiresUtc") Then Return False
            Dim expiresUtc As DateTime
            If Not DateTime.TryParse(data("ExpiresUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, expiresUtc) Then
                Return False
            End If

            If expiresUtc <= DateTime.UtcNow Then
                Try
                    File.Delete(path)
                Catch
                End Try
                Return False
            End If

            expiresLocal = expiresUtc.ToLocalTime()
            Return True

        Catch
            Return False
        End Try
    End Function

    ''' <summary>Returns a borrowed seat early. Only clears the local Borrow.lic on a
    ''' successful, parseable server response -- a failed/unreachable call leaves it in
    ''' place so offline use keeps working until the borrow naturally expires, and the
    ''' user can retry once back online.</summary>
    Public Function ReturnBorrowedSeat(ByRef message As String) As Boolean
        Dim path = GetBorrowLicensePath()
        If Not File.Exists(path) Then
            message = "No active borrow to return."
            Return False
        End If

        Dim data As Dictionary(Of String, String)
        Try
            data = ReadLicense(DecryptFile(path))
        Catch
            message = "Could not read the local borrow record."
            Return False
        End Try

        Dim token = If(data.ContainsKey("BorrowToken"), data("BorrowToken"), "")
        Dim url = If(data.ContainsKey("ServerUrl"), data("ServerUrl"), "")
        If token = "" OrElse url = "" Then
            message = "Local borrow record is incomplete."
            Return False
        End If

        Try
            Dim form As New Dictionary(Of String, String) From {{"seat_token", token}}
            Dim resp = _http.PostAsync($"{url}/release.php", New FormUrlEncodedContent(form)).GetAwaiter().GetResult()
            Dim json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            If Not json.TrimStart().StartsWith("{") Then
                message = "No response from the license server -- try again once you're back online."
                Return False
            End If
        Catch
            message = "Could not reach the license server -- try again once you're back online."
            Return False
        End Try

        Try
            File.Delete(path)
        Catch
        End Try
        message = "Returned successfully."
        Return True
    End Function

    Private Sub WriteBorrowLicense(uid As String, isActivationCode As Boolean, token As String, expiresUtc As DateTime)
        Dim machineId = GenerateLicenseCode()
        Dim data As New Dictionary(Of String, String) From {
            {"Product", AppProduct},
            {"Uid", uid},
            {"IsActivationCode", If(isActivationCode, "True", "False")},
            {"BorrowToken", token},
            {"ServerUrl", _serverUrl},
            {"StartedUtc", DateTime.UtcNow.ToString("O")},
            {"ExpiresUtc", expiresUtc.ToString("O")},
            {"MachineId", machineId}
        }
        For i = 0 To machineId.Length - 1
            data($"C{i}") = Asc(machineId(i)).ToString()
        Next

        EncryptToFile(SerializeLicense(data), GetBorrowLicensePath())
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

    ''' <summary>True if this install is set up to use a floating license at all — either via
    ''' a Floating .lic file or via a saved Activation Code — regardless of whether a seat
    ''' can currently be checked out.</summary>
    Public Function IsFloatingConfigured() As Boolean
        Return GetActivatedUid() IsNot Nothing OrElse IsFloatingLicense()
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

    Public Function GetBorrowLicensePath() As String
        Dim path = IOPath.Combine(GetLicenseDataDir(), "Borrow.lic")
        MigrateLegacyLicenseFileIfNeeded("Borrow.lic", path)
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
    ' NETWORK LICENSE SERVER — manual address entry + connectivity test
    ' =====================================================

    ''' <summary>
    ''' Parses a "host", "host:port", or "host@port" address (matching the Server
    ''' Name shown on the Network License Manager app), probes its checkout endpoint,
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

    ''' <summary>
    ''' The previously saved server address in the same "host:port" form the user
    ''' types into the Network License Server field, or "" if none is saved -- lets
    ''' LicenseWindow pre-fill that field instead of forcing a blind retype every
    ''' time activation is redone (e.g. after Change License clears the saved value),
    ''' where a typo/blank entry looks exactly like the server being unreachable.
    ''' </summary>
    Public Function GetLastServerAddress() As String
        Dim url = GetServerUrlOverride()
        If url Is Nothing Then Return ""

        Dim trimmed = url
        Const httpPrefix As String = "http://"
        If trimmed.StartsWith(httpPrefix, StringComparison.OrdinalIgnoreCase) Then
            trimmed = trimmed.Substring(httpPrefix.Length)
        End If

        Const apiSuffix As String = "/web/api/floating"
        If trimmed.EndsWith(apiSuffix, StringComparison.OrdinalIgnoreCase) Then
            trimmed = trimmed.Substring(0, trimmed.Length - apiSuffix.Length)
        End If

        Return trimmed
    End Function

    Private Function BuildServerUrl(rawAddress As String) As String
        Dim addr = If(rawAddress, "").Trim()
        If addr = "" Then Return Nothing

        ' Typing/pasting a full URL (e.g. "http://172.16.20.77:8080", possibly with
        ' the "/web/api/floating" path already on it, since that's exactly the
        ' internal format this same address gets saved/displayed in) is a completely
        ' natural thing to do -- without stripping this first, splitting on the first
        ' ":" below would parse "http" as the host and mangle the port entirely,
        ' silently building a URL that can never connect and reporting the server as
        ' unreachable even though it's fine. Strip scheme, any path, and a trailing
        ' slash so both the bare "host:port" and a full pasted URL resolve the same.
        Const httpPrefix As String = "http://"
        Const httpsPrefix As String = "https://"
        If addr.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase) Then
            addr = addr.Substring(httpsPrefix.Length)
        ElseIf addr.StartsWith(httpPrefix, StringComparison.OrdinalIgnoreCase) Then
            addr = addr.Substring(httpPrefix.Length)
        End If

        Dim slashIndex = addr.IndexOf("/"c)
        If slashIndex >= 0 Then addr = addr.Substring(0, slashIndex)
        addr = addr.Trim()
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

    ''' <summary>
    ''' Activates a floating license by Activation Code alone -- the single code an
    ''' admin sets on Network License Manager and distributes to every tool, covering
    ''' whatever products are currently loaded there -- against the given server
    ''' address, with no .lic file needed at all. Saves both so every future launch
    ''' checks out the same way (see CheckLicenseDetailed).
    ''' </summary>
    Public Function ActivateWithKey(rawUid As String, serverAddress As String, ByRef message As String) As Boolean
        Dim uid = If(rawUid, "").Trim()
        If uid = "" Then
            message = "Enter the Activation Code shown in Network License Manager."
            Return False
        End If

        Dim url = BuildServerUrl(serverAddress)
        If url Is Nothing Then
            message = "Enter a server address, e.g. vizserver:1122 or vizserver@1122."
            Return False
        End If

        SaveServerUrlOverride(url)
        SaveActivatedUid(uid)

        ' FloatingCheckoutDetailed reads the server address from the shared _serverUrl
        ' field, not a parameter -- without this, the very first Activate click in a
        ' fresh process posts to a bare "/checkout.php" with no host at all (relative
        ' URI, no BaseAddress on the HttpClient), which throws and gets caught as
        ' ServerUnreachable regardless of what was typed or how well it parsed. Every
        ' other caller of FloatingCheckoutDetailed (CheckLicenseDetailed's floating
        ' branches) already sets this first; this was the one path that didn't.
        _serverUrl = url

        Dim result = FloatingCheckoutDetailed(uid, GenerateLicenseCode(), isActivationCode:=True)
        LastActivationError = result
        If result = LicenseError.None Then
            ClearActivationPending()
            message = "Activated successfully."
            Return True
        End If

        ' Only a genuinely wrong code should un-stick the saved activation --
        ' NoSeatsAvailable/ServerUnreachable are transient, and ProductNotLicensed/
        ' AmbiguousCustomer mean the code itself was right but the server side isn't
        ' ready yet -- all should keep retrying on the next launch instead of forcing
        ' re-entry of a code the user already typed correctly. Since the code was
        ' accepted and saved despite the failure, flag it so the app can surface a
        ' "License activated successfully" message the moment a later retry finally
        ' gets a seat -- otherwise that first success is silent (see IsActivationPending).
        If result = LicenseError.InvalidLicense Then
            ClearActivatedUid()
        Else
            SetActivationPending()
        End If

        Select Case result
            Case LicenseError.NoSeatsAvailable
                message = $"No seats available ({LastSeatsInUse}/{LastSeatsMax} in use)."
            Case LicenseError.InvalidLicense
                message = "The server does not recognise this Activation Code."
            Case LicenseError.ServerUnreachable
                message = $"Could not reach a license server at {url}."
            Case LicenseError.WrongProduct
                message = "This Activation Code was issued for a different Adroitec product."
            Case LicenseError.ProductNotLicensed
                message = "This server doesn't have a license for this product loaded yet -- contact your admin."
            Case LicenseError.AmbiguousCustomer
                message = "This server has licenses loaded for more than one customer -- contact your admin."
            Case Else
                message = $"Activation failed ({result})."
        End Select
        Return False
    End Function

    Private Sub SaveActivatedUid(uid As String)
        Using k = Registry.CurrentUser.CreateSubKey(SettingsRegPath)
            k.SetValue("ActivatedFloatingUid", uid)
        End Using
    End Sub

    Private Sub SetActivationPending()
        Using k = Registry.CurrentUser.CreateSubKey(SettingsRegPath)
            k.SetValue("ActivationPendingConfirmation", "True")
        End Using
    End Sub

    ''' <summary>True if a floating activation was accepted and saved but never got a
    ''' confirmed seat -- the caller should show a one-time success message the moment
    ''' CheckLicense() next succeeds, then call ClearActivationPending().</summary>
    Public Function IsActivationPending() As Boolean
        Using k = Registry.CurrentUser.OpenSubKey(SettingsRegPath)
            Return If(k IsNot Nothing, TryCast(k.GetValue("ActivationPendingConfirmation"), String), Nothing) = "True"
        End Using
    End Function

    Public Sub ClearActivationPending()
        Using k = Registry.CurrentUser.OpenSubKey(SettingsRegPath, writable:=True)
            k?.DeleteValue("ActivationPendingConfirmation", throwOnMissingValue:=False)
        End Using
    End Sub

    Private Function GetActivatedUid() As String
        Using k = Registry.CurrentUser.OpenSubKey(SettingsRegPath)
            Dim v = If(k IsNot Nothing, TryCast(k.GetValue("ActivatedFloatingUid"), String), Nothing)
            Return If(String.IsNullOrWhiteSpace(v), Nothing, v)
        End Using
    End Function

    Private Sub ClearActivatedUid()
        Using k = Registry.CurrentUser.OpenSubKey(SettingsRegPath, writable:=True)
            k?.DeleteValue("ActivatedFloatingUid", throwOnMissingValue:=False)
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
