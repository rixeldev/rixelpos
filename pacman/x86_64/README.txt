RixelPOS pacman repository.

Add to /etc/pacman.conf:

[rixelpos]
SigLevel = Optional TrustAll
Server = https://rixeldev.github.io/rixelpos/pacman/x86_64

Then: sudo pacman -Syu rixelpos
