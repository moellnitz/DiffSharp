﻿//
// This file is part of
// DiffSharp -- F# Automatic Differentiation Library
//
// Copyright (C) 2014, National University of Ireland Maynooth.
//
//   DiffSharp is free software: you can redistribute it and/or modify
//   it under the terms of the GNU General Public License as published by
//   the Free Software Foundation, either version 3 of the License, or
//   (at your option) any later version.
//
//   DiffSharp is distributed in the hope that it will be useful,
//   but WITHOUT ANY WARRANTY; without even the implied warranty of
//   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//   GNU General Public License for more details.
//
//   You should have received a copy of the GNU General Public License
//   along with DiffSharp. If not, see <http://www.gnu.org/licenses/>.
//
// Written by:
//
//   Atilim Gunes Baydin
//   atilimgunes.baydin@nuim.ie
//
//   Barak A. Pearlmutter
//   barak@cs.nuim.ie
//
//   Hamilton Institute & Department of Computer Science
//   National University of Ireland Maynooth
//   Maynooth, Co. Kildare
//   Ireland
//
//   www.bcl.hamilton.ie
//

//
// Symbolic differentiation
//
// - Currently limited to closed-form algebraic functions, i.e. no control flow
// - Can drill into method bodies of other functions called from the current one, provided these have the [<ReflectedDefinition>] attribute set
// - Can compute higher order derivatives and all combinations of partial derivatives
//

#light

/// Symbolic differentiation module
module DiffSharp.Symbolic

open System.Reflection
open Microsoft.FSharp.Reflection
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns
open Microsoft.FSharp.Quotations.DerivedPatterns
open Microsoft.FSharp.Quotations.ExprShape
open FSharp.Quotations.Evaluator
open DiffSharp.Util.LinearAlgebra
open DiffSharp.Util.General
open DiffSharp.Util.Quotations

/// Symbolic differentiation expression operations module (automatically opened)
[<AutoOpen>]
module ExprOps =

    let coreass = typeof<unit>.Assembly
    let coremod = coreass.GetModule("FSharp.Core.dll")
    let coreops = coremod.GetType("Microsoft.FSharp.Core.Operators")
    let coreprim = coremod.GetType("Microsoft.FSharp.Core.LanguagePrimitives")

    let opAdd = coreops.GetMethod("op_Addition")
    let opSub = coreops.GetMethod("op_Subtraction")
    let opMul = coreops.GetMethod("op_Multiply")
    let opDiv = coreops.GetMethod("op_Division")
    let opPow = coreops.GetMethod("op_Exponentiation")
    let opNeg = coreops.GetMethod("op_UnaryNegation")
    let opLog = coreops.GetMethod("Log")
    let opExp = coreops.GetMethod("Exp")
    let opSin = coreops.GetMethod("Sin")
    let opCos = coreops.GetMethod("Cos")
    let opTan = coreops.GetMethod("Tan")
    let opSqrt = coreops.GetMethod("Sqrt")
    let opSinh = coreops.GetMethod("Sinh")
    let opCosh = coreops.GetMethod("Cosh")
    let opTanh = coreops.GetMethod("Tanh")
    let opAsin = coreops.GetMethod("Asin")
    let opAcos = coreops.GetMethod("Acos")
    let opAtan = coreops.GetMethod("Atan")
    let primGen0 = coreprim.GetMethod("GenericZero")
    let primGen1 = coreprim.GetMethod("GenericOne")

    let call(genmi:MethodInfo, types, args) =
        Expr.Call(genmi.MakeGenericMethod(Array.ofList types), args)

    let callGen0(t) =
        call(primGen0, [t], [])

    let callGen1(t) =
        call(primGen1, [t], [])

    let callGen2(t) =
        call(opAdd, [t; t; t], [callGen1(t); callGen1(t)])

    /// Recursively traverse and differentiate Expr `expr` with respect to Var `v`
    // UNOPTIMIZED
    let rec diffExpr (v:Var) expr =
        match expr with
        | Value(v, vt) -> callGen0(vt)
        | Call(_, primGen1, []) -> callGen0(v.Type)
        | SpecificCall <@ (+) @> (_, ts, [f; g]) -> call(opAdd, ts, [diffExpr v f; diffExpr v g])
        | SpecificCall <@ (-) @> (_, ts, [f; g]) -> call(opSub, ts, [diffExpr v f; diffExpr v g])
        | SpecificCall <@ (*) @> (_, ts, [f; g]) -> call(opAdd, ts, [call(opMul, ts, [diffExpr v f; g]); call(opMul, ts, [f; diffExpr v g])])
        | SpecificCall <@ (/) @> (_, ts, [f; g]) -> call(opDiv, ts, [call(opSub, ts, [call(opMul, ts, [diffExpr v f; g]); call(opMul, ts, [f; diffExpr v g])]); call(opMul, ts, [g; g])])
        //This should cover all the cases: (f(x) ^ (g(x) - 1))(g(x) * f'(x) + f(x) * log(f(x)) * g'(x))
        | SpecificCall <@ op_Exponentiation @> (_, [t1; t2], [f; g]) -> call(opMul, [t1; t1; t1], [call(opPow, [t1; t1], [f; call(opSub, [t1; t1; t1], [g; callGen1(t1)])]); call(opAdd, [t1; t1; t1], [call(opMul, [t1; t1; t1], [g; diffExpr v f]); call(opMul, [t1; t1; t1], [call(opMul, [t1; t1; t1], [f; call(opLog, [t1], [f])]); diffExpr v g])])])
        | SpecificCall <@ atan2 @> (_, [t1; t2], [f; g]) -> call(opDiv, [t1; t1; t1], [call(opSub, [t1; t1; t1], [call(opMul, [t1; t1; t1], [g; diffExpr v f]); call(opMul, [t1; t1; t1], [f; diffExpr v g])]) ; call(opAdd, [t1; t1; t1], [call(opMul, [t1; t1; t1], [f; f]); call(opMul, [t1; t1; t1], [g; g])])])
        | SpecificCall <@ (~-) @> (_, ts, [f]) -> call(opNeg, ts, [diffExpr v f])
        | SpecificCall <@ log @> (_, [t], [f]) -> call(opDiv, [t; t; t], [diffExpr v f; f])
        | SpecificCall <@ exp @> (_, [t], [f]) -> call(opMul, [t; t; t], [diffExpr v f; call(opExp, [t], [f])])
        | SpecificCall <@ sin @> (_, [t], [f]) -> call(opMul, [t; t; t], [diffExpr v f; call(opCos, [t], [f])])
        | SpecificCall <@ cos @> (_, [t], [f]) -> call(opMul, [t; t; t], [diffExpr v f; call(opNeg, [t], [call(opSin, [t], [f])])])
        | SpecificCall <@ tan @> (_, [t], [f]) -> call(opMul, [t; t; t], [diffExpr v f; call(opMul, [t; t; t], [call(opDiv, [t; t; t], [callGen1(t); call(opCos, [t], [f])]); call(opDiv, [t; t; t], [callGen1(t); call(opCos, [t], [f])])])])
        | SpecificCall <@ sqrt @> (_, [t1; t2], [f]) -> call(opDiv, [t1; t1; t1], [diffExpr v f; call(opMul, [t1; t1; t1], [callGen2(t1); call(opSqrt, [t1; t1], [f])])])
        | SpecificCall <@ sinh @> (_, [t], [f]) -> call(opMul, [t; t; t], [diffExpr v f; call(opCosh, [t], [f])])
        | SpecificCall <@ cosh @> (_, [t], [f]) -> call(opMul, [t; t; t], [diffExpr v f; call(opSinh, [t], [f])])
        | SpecificCall <@ tanh @> (_, [t], [f]) -> call(opMul, [t; t; t], [diffExpr v f; call(opMul, [t; t; t], [call(opDiv, [t; t; t], [callGen1(t); call(opCosh, [t], [f])]); call(opDiv, [t; t; t], [callGen1(t); call(opCosh, [t], [f])])])])
        | SpecificCall <@ asin @> (_, [t], [f]) -> call(opDiv, [t; t; t], [diffExpr v f; call(opSqrt, [t; t], [call(opSub, [t; t; t], [callGen1(t); call(opMul, [t; t; t], [f; f])])])])
        | SpecificCall <@ acos @> (_, [t], [f]) -> call(opDiv, [t; t; t], [diffExpr v f; call(opNeg, [t], [call(opSqrt, [t; t], [call(opSub, [t; t; t], [callGen1(t); call(opMul, [t; t; t], [f; f])])])])])
        | SpecificCall <@ atan @> (_, [t], [f]) -> call(opDiv, [t; t; t], [diffExpr v f; call(opAdd, [t; t; t], [callGen1(t); call(opMul, [t; t; t], [f; f])])])
        | ShapeVar(var) -> if var = v then callGen1(var.Type) else callGen0(var.Type)
        | ShapeLambda(arg, body) -> Expr.Lambda(arg, diffExpr v body)
        | ShapeCombination(shape, args) -> RebuildShapeCombination(shape, List.map (diffExpr v) args)
    
    /// Symbolically differentiate Expr `expr` with respect to variable name `vname`
    let diffSym vname expr =
        let eexpr = expr
        let args = getExprArgs eexpr
        let xvar = Array.tryFind (fun (a:Var) -> a.Name = vname) args
        match xvar with
        | Some(v) -> eexpr |> diffExpr v
        | None -> eexpr |> diffExpr (Var(vname, args.[0].Type))
    
    /// Compute the `n`-th derivative of an Expr, with respect to Var `v`
    let rec diffExprN v n =
        match n with
        | a when a < 0 -> failwith "Order of derivative cannot be negative."
        | 0 -> fun (x:Expr) -> x
        | 1 -> fun x -> diffExpr v x
        | _ -> fun x -> diffExprN v (n - 1) (diffExpr v x)

    /// Evaluate scalar-to-scalar Expr `expr`, at point `x`
    let evalSS (x:float) expr =
        Expr.Application(expr, Expr.Value(x))
        |> QuotationEvaluator.CompileUntyped
        :?> float

    /// Evaluate vector-to-scalar Expr `expr`, at point `x`
    let evalVS (x:float[]) expr =
        let args = List.ofArray x |> List.map (fun a -> [Expr.Value(a, typeof<float>)])
        Expr.Applications(expr, args)
        |> QuotationEvaluator.CompileUntyped
        :?> float
    
    /// Evaluate vector-to-vector Expr `expr`, at point `x`
    let evalVV (x:float[]) expr =
        let args = List.ofArray x |> List.map (fun a -> [Expr.Value(a, typeof<float>)])
        Expr.Applications(expr, args)
        |> QuotationEvaluator.CompileUntyped
        :?> float[]


/// Symbolic differentiation operations module (automatically opened)
[<AutoOpen>]
module SymbolicOps =
    /// First derivative of a scalar-to-scalar function `f`, at point `x`
    let diff (f:Expr<float->float>) x =
        let fe = expand f
        let args = getExprArgs fe
        diffExpr args.[0] fe
        |> evalSS x

    /// Original value and first derivative of a scalar-to-scalar function `f`, at point `x`
    let diff' f x =
        (evalSS x f, diff f x)

    /// `n`-th derivative of a scalar-to-scalar function `f`, at point `x`
    let diffn n (f:Expr<float->float>) x =
        let fe = expand f
        let args = getExprArgs fe
        diffExprN args.[0] n fe
        |> evalSS x

    /// Original value and `n`-th derivative of a scalar-to-scalar function `f`, at point `x`
    let diffn' n f x =
        (evalSS x f, diffn n f x)
    
    /// Second derivative of a scalar-to-scalar function `f`, at point `x`
    let diff2 f x =
        diffn 2 f x

    /// Original value and second derivative of a scalar-to-scalar function `f`, at point `x`
    let diff2' f x =
        (evalSS x f, diff2 f x)

    /// Original value, first derivative, and second derivative of a scalar-to-scalar function `f`, at point `x`
    let inline diff2'' f x =
        (evalSS x f, diff f x, diff2 f x)

    /// Gradient of a vector-to-scalar function `f`, at point `x`. Function should have multiple variables in curried form, instead of an array variable as in other parts of the library.
    let grad (f:Expr) x =
        let fe = expand f
        fe
        |> getExprArgs
        |> Array.map (fun a -> diffExpr a fe)
        |> Array.map (evalVS x)
    
    /// Original value and gradient of a vector-to-scalar function `f`, at point `x`. Function should have multiple variables in curried form, instead of an array variable as in other parts of the library.
    let grad' f x =
        (evalVS x f, grad f x)

    /// Transposed Jacobian of a vector-to-vector function `f`, at point `x`. Function should have multiple variables in curried form, instead of an array variable as in other parts of the library.
    let jacobianT (f:Expr) x =
        let fe = expand f
        fe
        |> getExprArgs
        |> Array.map (fun a -> diffExpr a fe)
        |> Array.map (evalVV x)
        |> array2D

    /// Original value and transposed Jacobian of a vector-to-vector function `f`, at point `x`. Function should have multiple variables in curried form, instead of an array variable as in other parts of the library.
    let jacobianT' f x =
        (evalVV x f, jacobianT f x)

    /// Jacobian of a vector-to-vector function `f`, at point `x`. Function should have multiple variables in curried form, instead of an array variable as in other parts of the library.
    let jacobian f x =
        jacobianT f x |> transpose

    /// Original value and Jacobian of a vector-to-vector function `f`, at point `x`. Function should have multiple variables in curried form, instead of an array variable as in other parts of the library.
    let jacobian' f x =
        jacobianT' f x |> fun (r, j) -> (r, transpose j)

    /// Laplacian of a vector-to-scalar function `f`, at point `x`. Function should have multiple variables in curried form, instead of an array variable as in other parts of the library.
    let laplacian (f:Expr) x =
        let fe = expand f
        fe
        |> getExprArgs
        |> Array.map (fun a -> diffExpr a (diffExpr a fe))
        |> Array.sumBy (evalVS x)

    /// Original value and Laplacian of a vector-to-scalar function `f`, at point `x`. Function should have multiple variables in curried form, instead of an array variable as in other parts of the library.
    let laplacian' f x =
        (evalVS x f, laplacian f x)

    /// Hessian of a vector-to-scalar function `f`, at point `x`. Function should have multiple variables in curried form, instead of an array variable as in other parts of the library.
    let hessian (f:Expr) (x:float[]) =
        let fe = expand f
        let args = getExprArgs fe
        let ret:float[,] = Array2D.create x.Length x.Length 0.
        for i = 0 to x.Length - 1 do
            let di = diffExpr args.[i] fe
            for j = i to x.Length - 1 do
                ret.[i, j] <- evalVS x (diffExpr args.[j] di)
        copyupper ret

    /// Original value and Hessian of a vector-to-scalar function `f`, at point `x`. Function should have multiple variables in curried form, instead of an array variable as in other parts of the library.
    let hessian' f x =
        (evalVS x f, hessian f x)

    /// Original value, gradient, and Hessian of a vector-to-scalar function `f`, at point `x`. Function should have multiple variables in curried form, instead of an array variable as in other parts of the library.
    let gradhessian' f x =
        let (v, g) = grad' f x in (v, g, hessian f x)

    /// Gradient and Hessian of a vector-to-scalar function `f`, at point `x`. Function should have multiple variables in curried form, instead of an array variable as in other parts of the library.
    let gradhessian f x =
        gradhessian' f x |> sndtrd


/// Module with differentiation operators using Vector and Matrix input and output, instead of float[] and float[,]
module Vector =
    /// Original value and first derivative of a scalar-to-scalar function `f`, at point `x`
    let inline diff' f x = SymbolicOps.diff' f x
    /// First derivative of a scalar-to-scalar function `f`, at point `x`
    let inline diff f x = SymbolicOps.diff f x
    /// Original value and second derivative of a scalar-to-scalar function `f`, at point `x`
    let inline diff2' f x = SymbolicOps.diff2' f x
    /// Second derivative of a scalar-to-scalar function `f`, at point `x`
    let inline diff2 f x = SymbolicOps.diff2 f x
    /// Original value, first derivative, and second derivative of a scalar-to-scalar function `f`, at point `x`
    let inline diff2'' f x = SymbolicOps.diff2'' f x
    /// Original value and the `n`-th derivative of a scalar-to-scalar function `f`, at point `x`
    let inline diffn' n f x = SymbolicOps.diffn' n f x
    /// `n`-th derivative of a scalar-to-scalar function `f`, at point `x`
    let inline diffn n f x = SymbolicOps.diffn n f x
    /// Original value and gradient of a vector-to-scalar function `f`, at point `x`
    let inline grad' f x = SymbolicOps.grad' f (array x) |> fun (a, b) -> (a, vector b)
    /// Gradient of a vector-to-scalar function `f`, at point `x`
    let inline grad f x = SymbolicOps.grad f (array x) |> vector
    /// Original value and Laplacian of a vector-to-scalar function `f`, at point `x`
    let inline laplacian' f x = SymbolicOps.laplacian' f (array x)
    /// Laplacian of a vector-to-scalar function `f`, at point `x`
    let inline laplacian f x = SymbolicOps.laplacian f (array x)
    /// Original value and transposed Jacobian of a vector-to-vector function `f`, at point `x`
    let inline jacobianT' f x = SymbolicOps.jacobianT' f (array x) |> fun (a, b) -> (vector a, matrix b)
    /// Transposed Jacobian of a vector-to-vector function `f`, at point `x`
    let inline jacobianT f x = SymbolicOps.jacobianT f (array x) |> matrix
    /// Original value and Jacobian of a vector-to-vector function `f`, at point `x`
    let inline jacobian' f x = SymbolicOps.jacobian' f (array x) |> fun (a, b) -> (vector a, matrix b)
    /// Jacobian of a vector-to-vector function `f`, at point `x`
    let inline jacobian f x = SymbolicOps.jacobian f (array x) |> matrix
    /// Original value and Hessian of a vector-to-scalar function `f`, at point `x`
    let inline hessian' f x = SymbolicOps.hessian' f (array x) |> fun (a, b) -> (a, matrix b)
    /// Hessian of a vector-to-scalar function `f`, at point `x`
    let inline hessian f x = SymbolicOps.hessian f (array x) |> matrix
    /// Original value, gradient, and Hessian of a vector-to-scalar function `f`, at point `x`
    let inline gradhessian' f x = SymbolicOps.gradhessian' f (array x) |> fun (a, b, c) -> (a, vector b, matrix c)
    /// Gradient and Hessian of a vector-to-scalar function `f`, at point `x`
    let inline gradhessian f x = SymbolicOps.gradhessian f (array x) |> fun (a, b) -> (vector a, matrix b)