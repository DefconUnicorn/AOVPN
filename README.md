<center><img width="637" height="953" alt="always on vpn" src="https://github.com/user-attachments/assets/7adade02-3124-4d51-af34-fb31f24048da" />
</center><br>
# AOVPN

Location-aware Windows Always On VPN client using OpenVPN DCO.

The desktop client and service use separate pre-login and post-login profiles,
detect the internal network through IPv4 DNS, and require the Windows OpenVPN
DCO driver. See `desktop/README.md` and `test-package/AOVPN/README.txt` for
build and test instructions.

## Licensing

AOVPN is licensed under GPLv2. OpenVPN core and third-party runtime components
retain their own licenses. See `LICENSES/THIRD-PARTY-NOTICES.md` and the files
in `LICENSES/` for the notices distributed with the test package.

OpenVPN is a trademark of OpenVPN Inc. This project is not affiliated with or
endorsed by OpenVPN Inc.

#FIREWALL \ VPN SERVER SETUP PFSESNE EXAMPLE

Create Cert Auth if you dont have on already (System > Certificates).

VPN > OpenVPN add servers:
<img width="1154" height="260" alt="{6E7DFDF8-ABF7-4641-B0D5-49BBEBEC6288}" src="https://github.com/user-attachments/assets/60ebe2fa-fe20-4f86-b333-b0a05b3ef4c5" />

#PRE-AUTH (Windows login screen)
UPD IPv4 Only
Interface WAN
Remote Acecss (SSL \ TLS)
1194
Peer Certificate Authority (the one you just created)
Data Encryption Algorithms AES-256-GCM
Fallback Data Encryption Algorithm AES-256-GCM
IPv4 Tunnel Network 10.10.8.0/24
IPv4 Local network(s) (You internal network range 192.168.1.0/24)
Duplicate Connection allow
Duplicate Connection Limit (number of devices you have)
Dynamic IP allow
DNS Default Domain yes
DNS Default Domain (your AD domain)
DNS Server enable yes
DNS Server 1 (Your AD Server)
Force DNS cache update (yes, might help)
UDP Fast I/O (yes unless you have problems)
Gateway creation IPv4 only

#POST AUTH (User Desktop)
UPD IPv4 Only
Interface WAN
Remote Acecss (SSL \ TLS) + User Auth
1195
Peer Certificate Authority (the one you just created)
Data Encryption Algorithms AES-256-GCM
Fallback Data Encryption Algorithm AES-256-GCM
IPv4 Tunnel Network 10.10.9.0/24
IPv4 Local network(s) (You internal network range 192.168.1.0/24)
Duplicate Connection allow
Duplicate Connection Limit (number of devices you have)
Dynamic IP allow
DNS Default Domain yes
DNS Default Domain (your AD domain)
DNS Server enable yes
DNS Server 1 (Your AD Server)
Force DNS cache update (yes, might help)
UDP Fast I/O (yes unless you have problems)
Gateway creation IPv4 only

#Firewall WAN
Open UDP ports on wan 1194-1195

#Interfaces > Assignments
Add both PRE and POST to new interfaces (this allows us to set diffent rules for each)
Do not set any rules under firewall > rules > OpenVPN (These will overide as they apply before the interface rules).
<img width="1034" height="253" alt="{21D3F53B-8087-4F63-9205-E27EA287DEA2}" src="https://github.com/user-attachments/assets/4ea603ab-ad8a-49df-a49b-83501b5d6436" />


#PREAUTH Interface
Good idea to add a block all rule have it sit at the bottom as rules apply top down.
Then allow where destination is your AD server IP and pick from the below ports, create a alias for UPD ports and one for TCP ports.

DNS (Domain Name System) - TCP and UDP Port 53
Explanation: The desktop uses DNS to look up the IP addresses of your Active Directory Domain Controllers. Without this, the computer cannot locate the domain on the network.
Kerberos Authentication - TCP and UDP Port 88
Explanation: This is the primary protocol Windows uses to log users in and authenticate the computer itself. It validates credentials and issues security tickets.
NTP (Network Time Protocol) - UDP Port 123
Explanation: Windows Time service uses this to synchronize the desktop's clock with the Domain Controller. Kerberos authentication will strictly fail if the time difference between the client and the DC is more than five minutes.
RPC Endpoint Mapper - TCP Port 135
Explanation: Active Directory relies heavily on Remote Procedure Calls (RPC). This port acts as a directory service that tells the desktop which random high-numbered port to use for specific backend communication.
LDAP (Lightweight Directory Access Protocol) - TCP and UDP Port 389
Explanation: The desktop uses LDAP to query the Active Directory database for information about user accounts, computer accounts, and group memberships.
SMB (Server Message Block) - TCP Port 445
Explanation: Group Policy objects are physical files stored in a shared folder on the Domain Controller called SYSVOL. The desktop connects over SMB to read and download these policy files so they can be applied to the system.
Kerberos Password Change - TCP and UDP Port 464
Explanation: This port handles password changes and account resets. If a user's password expires or they try to change it from the desktop, this port is required.
LDAP over SSL/TLS - TCP Port 636
Explanation: This is used instead of standard LDAP if your organization requires all directory queries to be encrypted over an SSL connection.
Active Directory Web Services - TCP Port 9389
Explanation: Used by newer Windows management components, automated scripts, and PowerShell commands running on the client to interact with the directory.
RPC Dynamic Ephemeral Ports - TCP Ports 49152 through 65535
Explanation: After the desktop talks to the RPC Endpoint Mapper on Port 135, the Domain Controller assigns a random port within this high range to handle the actual data transfer for Group Policy processing and authentication traffic.

#POSTAUTH Interface
You may want to limit access to server IPs or just allow all.
This is really up to you.





