﻿namespace FVim

open Avalonia.Media
open ReactiveUI
open FVim.neovim.def
open FVim.common.helpers

type CursorViewModel(cursorMode: int option) =
    inherit ViewModelBase(None, None, Some 1.0, Some 1.0)

    member val enabled: bool       = true with get,set
    member val ingrid: bool        = true with get,set
    member val row: int            = 0 with get,set
    member val col: int            = 0 with get,set
    member val modeidx: int        = _d -1 cursorMode with get,set
    member val typeface: string    = "" with get,set
    member val wtypeface: string   = "" with get,set
    member val fontSize: float     = 8.0 with get,set
    member val text: string        = "" with get,set
    member val fg: Color           = Colors.White with get,set
    member val bg: Color           = Colors.Black with get,set
    member val sp: Color           = Colors.Red with get,set
    member val underline: bool     = false with get,set
    member val undercurl: bool     = false with get,set
    member val bold: bool          = false with get,set
    member val italic: bool        = false with get,set
    member val blinkon: int        = 0 with get,set
    member val blinkoff: int       = 0 with get,set
    member val blinkwait: int      = 0 with get,set
    member val cellPercentage: int = 100 with get,set
    member val shape: CursorShape  = CursorShape.Block with get,set
    
    member this.Clone() =
        this.MemberwiseClone() :?> CursorViewModel

    member this.VisualChecksum() =
        seq [
            hash this.typeface
            hash this.wtypeface
            hash this.fontSize
            hash this.Height
            hash this.Width
        ] |> Seq.fold (^^^) 0

