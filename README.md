# AmeowCoin [AMEOW]

![AmeowCoin](Images/Icon.png)

Ameow is a **toy cryptocurrency**. It was made as a hobby project and
named by its author's girlfriend.

This project doesn't aim to be a robust and full-featured cryptocurrency.
I made it just to see how the technology works. You can build it, run it
on some servers, mine some coins and even sends those coins to your friends.
You can take a look at the source code to see what's under the hood.
But in the end, it is just for fun.

By the way, 1/100,000,000 of an *AMEOW* is called a *nekoshi*.

## Implementation

Many aspects of cryptocurrency have been simplified to help speeding up
the development and comprehension of the project.

* AmeowCoin uses Proof-of-Work with SHA256 as the hashing algorithm.
* Difficulty of a block is the number of leading 0 bits in the hash.
* Block time distance is based on block height.
* Block reward starts at 64 AMEOW and is cut in halves every 10,000 blocks.
* Transaction fee is fixed at 0.5 AMEOW per transaction.
* Block has a maximum of 32 transactions.
* Transaction has a maximum of 32 inputs.
* Network and storage representation is JSON.
* Blockchain conflicts are solved in a keep-it-simple-stupid way.
* NO SECURITY.

## Assemblies

### Ameow

This is the core library.

Below are some notable classes and namespaces:

* `App`: the application core.
* `ChainManager`: the blockchain.
* `Pow`: the Proof-of-Work helper class.
* `Config`: coin configurations.
* `Wallet`: wallet manager.
* `Network` namespace: TCP communication between nodes.
* `Storage` namespace: loading and storing of blocks and transactions.

### AmeowCLI

The command-line interactive client.

### AmeowGUI

The Windows Forms client.

## Build

The project source code is written in C# 9 and built using Visual Studio 2019.

Current target framework is .NET 5, which can be downloaded from
https://dotnet.microsoft.com/download.

**Ameow** and **AmeowCLI** supports: Windows, Linux, MacOS (not tested yet).

**AmeowGUI** supports Windows only, for obvious reasons.

## Run

### Seed nodes

There should be a file named `peers.txt` in the same directory of the executable files. This file contains addresses of seed nodes.

Sample content:

```
100.100.100.100:6789
200.200.200.200:6789
```

### ameow-cli

The client support the following parameters:

* `-p` or `--port`: port for listening to remote peers.
If this parameter exists, the client will not enable interactive mode.

* `--log`: log mode, accepts `file` (default), `console` and `both`.

Examples:

```sh
# run as a normal client with interactive prompt
./ameow-cli

# run as a node at port 6789
./ameow-cli -p 6789

# run as a node and print logs to terminal only
./ameow-cli -p 6789 --log console
```

When running with interactive mode, enter `help` for more information.

### ameow-gui

Currently a very basic GUI client with only Send and Mine features.
You will easily know how to use it.

## Contribute

If you find something that can be fixed or improved, please feel free
to open an issue or send a pull request. I will reply when I have some free time.

If you contribute code to this project, you are implicitly allowing your code
to be distributed under the MIT license. You are also implicitly
verifying that all code is your original work.
