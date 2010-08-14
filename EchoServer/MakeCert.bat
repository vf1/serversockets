"C:\Program Files\Microsoft SDKs\Windows\v6.0A\bin\makecert" -r -pe -n "CN=SocketServers" -b 01/01/2010 -e 01/01/2020 -sky exchange SocketServers.cer -sv SocketServers.pvk
"C:\Program Files\Microsoft SDKs\Windows\v6.0A\bin\pvk2pfx.exe" -pvk SocketServers.pvk -spc SocketServers.cer -pfx SocketServers.pfx
