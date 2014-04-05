#!/usr/bin/env bash

echo $'\n==================='
echo $'Getting updates ...'
echo $'===================\n'

sudo apt-get -y update
sudo apt-get install -y g++
sudo apt-get install -y make
sudo apt-get install -y python-software-properties python
sudo apt-get install -y automake autoconf
sudo apt-get install -y libtool libgmp-dev
sudo apt-get install -y wget git subversion mercurial
sudo apt-get install -y gettext zlib1g-dev asciidoc libcurl4-openssl-dev 

export PROJECT_ROOT=/vagrant
export BUILD_ROOT=/home/vagrant/whoop
export CMAKE_VERSION=2.8.8
export MONO_VERSION=3.0.7
# export LLVM_RELEASE=34
export LLVM_RELEASE=release_34

mkdir -p ${BUILD_ROOT}

echo $'\n======================'
echo $'Getting latest git ...'
echo $'======================\n'

cd ${BUILD_ROOT}
git clone https://github.com/git/git.git 

echo $'\n======================='
echo $'Building latest git ...'
echo $'=======================\n'

cd git
make configure 
./configure --prefix=/usr
make all doc
sudo make install install-doc install-html 

echo $'\n================='
echo $'Getting CMAKE ...'
echo $'=================\n'

cd ${BUILD_ROOT}
wget http://www.cmake.org/files/v2.8/cmake-${CMAKE_VERSION}-Linux-i386.tar.gz

echo $'\n==================='
echo $'Unpacking CMAKE ...'
echo $'===================\n'

tar zxvf cmake-${CMAKE_VERSION}-Linux-i386.tar.gz
rm cmake-${CMAKE_VERSION}-Linux-i386.tar.gz
export PATH=${BUILD_ROOT}/cmake-${CMAKE_VERSION}-Linux-i386/bin:$PATH

echo $'\n======================='
echo $'Unpacking CMAKE done ...'
echo $'=======================\n'

echo $'\n================'
echo $'Getting MONO ...'
echo $'================\n'

cd ${BUILD_ROOT}
wget http://download.mono-project.com/sources/mono/mono-${MONO_VERSION}.tar.bz2

echo $'\n=================='
echo $'Unpacking MONO ...'
echo $'==================\n'

tar jxf mono-${MONO_VERSION}.tar.bz2
rm mono-${MONO_VERSION}.tar.bz2

echo $'\n======================'
echo $'Unpacking MONO done ...'
echo $'======================\n'

echo $'\n================='
echo $'Building MONO ...'
echo $'=================\n'

cd ${BUILD_ROOT}/mono-${MONO_VERSION}
./configure --prefix=${BUILD_ROOT}/local --with-large-heap=yes --enable-nls=no
make -j4
make install
export PATH=${BUILD_ROOT}/local/bin:$PATH

echo $'\n================'
echo $'Getting LLVM ...'
echo $'================\n'

mkdir -p ${BUILD_ROOT}/llvm_and_clang
cd ${BUILD_ROOT}/llvm_and_clang
git clone https://github.com/llvm-mirror/llvm.git src
cd ${BUILD_ROOT}/llvm_and_clang/src
git checkout release_34
# svn co http://llvm.org/svn/llvm-project/llvm/branches/release_${LLVM_RELEASE} src
cd ${BUILD_ROOT}/llvm_and_clang/src/tools
git clone https://github.com/llvm-mirror/clang.git clang
cd ${BUILD_ROOT}/llvm_and_clang/src/tools/clang
git checkout release_34
# svn co http://llvm.org/svn/llvm-project/cfe/branches/release_${LLVM_RELEASE} clang
cd ${BUILD_ROOT}/llvm_and_clang/src/projects
git clone https://github.com/llvm-mirror/compiler-rt.git compiler-rt
cd ${BUILD_ROOT}/llvm_and_clang/src/projects/compiler-rt
git checkout release_34
# svn co http://llvm.org/svn/llvm-project/compiler-rt/branches/release_${LLVM_RELEASE} compiler-rt

echo $'\n================='
echo $'Building LLVM ...'
echo $'=================\n'

mkdir -p ${BUILD_ROOT}/llvm_and_clang/build
cd ${BUILD_ROOT}/llvm_and_clang/build
cmake -D CMAKE_BUILD_TYPE=Release ../src
make -j4

echo $'\n================='
echo $'Getting SMACK ...'
echo $'=================\n'

cd ${BUILD_ROOT}
git clone https://github.com/smackers/smack.git ${BUILD_ROOT}/smack/src

echo $'\n=================='
echo $'Building SMACK ...'
echo $'==================\n'

mkdir -p ${BUILD_ROOT}/smack/build
cd ${BUILD_ROOT}/smack/build
cmake -D LLVM_CONFIG=${BUILD_ROOT}/llvm_and_clang/build/bin -D CMAKE_BUILD_TYPE=Release -D CMAKE_INSTALL_PREFIX=${BUILD_ROOT}/smack/install ../src
make -j4
make install

echo $'\n=============='
echo $'Getting Z3 ...'
echo $'==============\n'

cd ${BUILD_ROOT}
git clone https://git01.codeplex.com/z3

echo $'\n==============='
echo $'Building Z3 ...'
echo $'===============\n'

cd ${BUILD_ROOT}/z3
autoconf
./configure
python scripts/mk_make.py
cd build
make -j4
make install
ln -s z3 z3.exe

echo $'\n================'
echo $'Getting CVC4 ...'
echo $'================\n'

cd ${BUILD_ROOT}
git clone https://github.com/CVC4/CVC4.git ${BUILD_ROOT}/CVC4/src

echo $'\n================='
echo $'Building CVC4 ...'
echo $'=================\n'

cd ${BUILD_ROOT}/CVC4/src
MACHINE_TYPE=$1 contrib/get-antlr-3.4
./autogen.sh
export ANTLR=${BUILD_ROOT}/CVC4/src/antlr-3.4/bin/antlr3
./configure --with-antlr-dir=${BUILD_ROOT}/CVC4/src/antlr-3.4 \
	--prefix=${BUILD_ROOT}/CVC4/install \
	--best --enable-gpl \
	--disable-shared --enable-static
make -j4
make install
cd ${BUILD_ROOT}/CVC4/install/bin
ln -s cvc4 cvc4.exe

echo $'\n================='
echo $'Getting WHOOP ...'
echo $'=================\n'

cd ${BUILD_ROOT}
cp -a ${PROJECT_ROOT}/. ${BUILD_ROOT}/whoop/

echo $'\n======================'
echo $'Building CHAUFFEUR ...'
echo $'======================\n'

cd ${BUILD_ROOT}/whoop
mkdir -p ${BUILD_ROOT}/whoop/FrontEndPlugin/build
cd ${BUILD_ROOT}/whoop/FrontEndPlugin/build
cmake -D LLVM_CONFIG=${BUILD_ROOT}/llvm_and_clang/build/bin -D CMAKE_BUILD_TYPE=Release ../src
make -j4

echo $'\n=================='
echo $'Building WHOOP ...'
echo $'==================\n'

cd ${BUILD_ROOT}/whoop
xbuild /p:TargetFrameworkProfile="" /p:Configuration=Debug whoop.sln

echo $'\n====================='
echo $'Configuring WHOOP ...'
echo $'=====================\n'

cp /Scripts/Vagrant/findtools.vagrant.py findtools.py

echo $'\n========'
echo $'Done ...'
echo $'========\n'
