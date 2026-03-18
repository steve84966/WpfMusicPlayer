package OpenSSL::safe::installdata;

use strict;
use warnings;
use Exporter;
our @ISA = qw(Exporter);
our @EXPORT = qw(
    @PREFIX
    @libdir
    @BINDIR @BINDIR_REL_PREFIX
    @LIBDIR @LIBDIR_REL_PREFIX
    @INCLUDEDIR @INCLUDEDIR_REL_PREFIX
    @APPLINKDIR @APPLINKDIR_REL_PREFIX
    @ENGINESDIR @ENGINESDIR_REL_LIBDIR
    @MODULESDIR @MODULESDIR_REL_LIBDIR
    @PKGCONFIGDIR @PKGCONFIGDIR_REL_LIBDIR
    @CMAKECONFIGDIR @CMAKECONFIGDIR_REL_LIBDIR
    $VERSION @LDLIBS
);

our @PREFIX                     = ( 'C:\Users\madoka\source\repos\PlaygroundTest\MusicPlayerLibrary\vcpkg_installed\x64-windows\vcpkg\blds\openssl\x64-windows-dbg' );
our @libdir                     = ( '' );
our @BINDIR                     = ( 'C:\Users\madoka\source\repos\PlaygroundTest\MusicPlayerLibrary\vcpkg_installed\x64-windows\vcpkg\blds\openssl\x64-windows-dbg\apps' );
our @BINDIR_REL_PREFIX          = ( 'apps' );
our @LIBDIR                     = ( 'C:\Users\madoka\source\repos\PlaygroundTest\MusicPlayerLibrary\vcpkg_installed\x64-windows\vcpkg\blds\openssl\x64-windows-dbg' );
our @LIBDIR_REL_PREFIX          = ( '' );
our @INCLUDEDIR                 = ( 'C:\Users\madoka\source\repos\PlaygroundTest\MusicPlayerLibrary\vcpkg_installed\x64-windows\vcpkg\blds\openssl\x64-windows-dbg\include', 'C:\Users\madoka\source\repos\PlaygroundTest\MusicPlayerLibrary\vcpkg_installed\x64-windows\vcpkg\blds\openssl\x64-windows-dbg\include' );
our @INCLUDEDIR_REL_PREFIX      = ( 'include', './include' );
our @APPLINKDIR                 = ( 'C:\Users\madoka\source\repos\PlaygroundTest\MusicPlayerLibrary\vcpkg_installed\x64-windows\vcpkg\blds\openssl\x64-windows-dbg\ms' );
our @APPLINKDIR_REL_PREFIX      = ( 'ms' );
our @ENGINESDIR                 = ( 'C:\Users\madoka\source\repos\PlaygroundTest\MusicPlayerLibrary\vcpkg_installed\x64-windows\vcpkg\blds\openssl\x64-windows-dbg\engines' );
our @ENGINESDIR_REL_LIBDIR      = ( 'engines' );
our @MODULESDIR                 = ( 'C:\Users\madoka\source\repos\PlaygroundTest\MusicPlayerLibrary\vcpkg_installed\x64-windows\vcpkg\blds\openssl\x64-windows-dbg\providers' );
our @MODULESDIR_REL_LIBDIR      = ( 'providers' );
our @PKGCONFIGDIR               = ( 'C:\Users\madoka\source\repos\PlaygroundTest\MusicPlayerLibrary\vcpkg_installed\x64-windows\vcpkg\blds\openssl\x64-windows-dbg' );
our @PKGCONFIGDIR_REL_LIBDIR    = ( '.' );
our @CMAKECONFIGDIR             = ( 'C:\Users\madoka\source\repos\PlaygroundTest\MusicPlayerLibrary\vcpkg_installed\x64-windows\vcpkg\blds\openssl\x64-windows-dbg' );
our @CMAKECONFIGDIR_REL_LIBDIR  = ( '.' );
our $VERSION                    = '3.6.1';
our @LDLIBS                     =
    # Unix and Windows use space separation, VMS uses comma separation
    $^O eq 'VMS'
    ? split(/ *, */, 'ws2_32.lib gdi32.lib advapi32.lib crypt32.lib user32.lib ')
    : split(/ +/, 'ws2_32.lib gdi32.lib advapi32.lib crypt32.lib user32.lib ');

1;
