<div align="center">
  <img width="637" height="953" alt="Always On VPN" src="https://github.com/user-attachments/assets/7adade02-3124-4d51-af34-fb31f24048da" />
</div>

# AOVPN

Location-aware Windows Always On VPN client using OpenVPN DCO.

The desktop client and service use separate pre-login and post-login profiles,
detect the internal network through IPv4 DNS, and require the Windows OpenVPN
DCO driver. See `desktop/README.md` and `test-package/AOVPN/README.txt` for
build and test instructions. The release MSI also installs the signed OpenVPN
DCO driver for x64 Windows machines.

## Customis GUI
[https://github.com/DefconUnicorn/AOVPN/blob/master/desktop/README.md]

## Installer

The release MSI is a self-contained per-machine installer that registers the
automatic `AOVPNService` Windows service. It supports interactive installation,
silent deployment, and Group Policy software deployment. See
`installer/README.md` for build and `msiexec` commands.

## Licensing

AOVPN is licensed under GPLv2. OpenVPN core and third-party runtime components
retain their own licenses. See `LICENSES/THIRD-PARTY-NOTICES.md` and the files
in `LICENSES/` for the notices distributed with the test package.

OpenVPN is a trademark of OpenVPN Inc. This project is not affiliated with or
endorsed by OpenVPN Inc.

## Firewall / VPN Server Setup: pfSense Example

Create certificate authentication if you do not have it already:
**System > Certificates**.

Under **VPN > OpenVPN**, add the following servers.

<img width="1154" height="260" alt="OpenVPN server settings" src="https://github.com/user-attachments/assets/60ebe2fa-fe20-4f86-b333-b0a05b3ef4c5" />

### Pre-Auth: Windows Login Screen

- **UDP IPv4 only**
- **Interface:** WAN
- **Remote access:** SSL/TLS
- **Port:** `1194`
- **Peer Certificate Authority:** the CA created above
- **Data Encryption Algorithms:** AES-256-GCM
- **Fallback Data Encryption Algorithm:** AES-256-GCM
- **IPv4 Tunnel Network:** `10.10.8.0/24`
- **IPv4 Local network(s):** your internal network range, for example `192.168.1.0/24`
- **Duplicate Connections:** allow
- **Duplicate Connection Limit:** the number of devices you have
- **Dynamic IP:** allow
- **DNS Default Domain:** enabled
- **DNS Default Domain:** your AD domain
- **DNS Server:** enabled
- **DNS Server 1:** your AD server
- **Force DNS cache update:** enabled, if needed
- **UDP Fast I/O:** enabled unless you have problems
- **Gateway creation:** IPv4 only

### Post-Auth: User Desktop

- **UDP IPv4 only**
- **Interface:** WAN
- **Remote access:** SSL/TLS plus user authentication
- **Port:** `1195`
- **Peer Certificate Authority:** the CA created above
- **Data Encryption Algorithms:** AES-256-GCM
- **Fallback Data Encryption Algorithm:** AES-256-GCM
- **IPv4 Tunnel Network:** `10.10.9.0/24`
- **IPv4 Local network(s):** your internal network range, for example `192.168.1.0/24`
- **Duplicate Connections:** allow
- **Duplicate Connection Limit:** the number of devices you have
- **Dynamic IP:** allow
- **DNS Default Domain:** enabled
- **DNS Default Domain:** your AD domain
- **DNS Server:** enabled
- **DNS Server 1:** your AD server
- **Force DNS cache update:** enabled, if needed
- **UDP Fast I/O:** enabled unless you have problems
- **Gateway creation:** IPv4 only

## Firewall WAN

Open UDP ports `1194-1195` on the WAN firewall.

## Interfaces > Assignments

Add both PRE and POST to new interfaces. This allows separate rules for each
interface.

Do not set rules under **Firewall > Rules > OpenVPN**. Those rules override the
interface rules because they apply before the interface rules.

<img width="1034" height="253" alt="PRE and POST interface assignments" src="https://github.com/user-attachments/assets/4ea603ab-ad8a-49df-a49b-83501b5d6436" />

## PREAUTH Interface

Add a block-all rule at the bottom because firewall rules are applied
top-to-bottom. Then allow traffic to your AD server IP using the ports below.
Create one alias for UDP ports and one alias for TCP ports.

- **DNS (Domain Name System):** TCP and UDP `53`<br>
  The desktop uses DNS to find the IP addresses of Active Directory domain controllers.
- **Kerberos Authentication:** TCP and UDP `88`<br>
  Windows uses this protocol to authenticate the computer and issue security tickets.
- **NTP (Network Time Protocol):** UDP `123`<br>
  Windows Time uses this to synchronize with the domain controller. Kerberos can fail when the clock differs by more than five minutes.
- **RPC Endpoint Mapper:** TCP `135`<br>
  Active Directory uses this to identify the dynamic port needed for backend communication.
- **LDAP (Lightweight Directory Access Protocol):** TCP and UDP `389`<br>
  The desktop uses LDAP to query Active Directory.
- **SMB (Server Message Block):** TCP `445`<br>
  Group Policy files are stored in SYSVOL and are read over SMB.
- **Kerberos Password Change:** TCP and UDP `464`<br>
  This handles password changes and account resets.
- **LDAP over SSL/TLS:** TCP `636`<br>
  Use this when directory queries must be encrypted.
- **Active Directory Web Services:** TCP `9389`<br>
  Newer Windows management components and PowerShell use this service.
- **RPC Dynamic Ephemeral Ports:** TCP `49152-65535`<br>
  The domain controller assigns a dynamic port after the RPC endpoint mapper is contacted.

## POSTAUTH Interface

You may want to limit access to server IPs or allow all traffic. This is up to
your network policy.
