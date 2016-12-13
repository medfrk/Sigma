﻿/* 
MIT License

Copyright (c) 2016 Florian Cäsar, Michael Plainer

For full license see LICENSE in the root directory of this project. 
*/

using System;
using System.Runtime.InteropServices;

namespace Sigma.Core.Handlers.Backends.DiffSharp.NativeCpu
{
	/// <summary>
	/// An OpenBLAS partial LAPACK backend using external libopenblas native functions.
	/// </summary>
	public class OpenBlasLapackBackend : ILapackBackend
	{
		public unsafe void Sgesv(int* n, int* nrhs, float* a, int* lda, int* ipiv, float* b, int* ldb, int* info)
		{
			NativeOpenBlasLapackMethods.sgesv_(n, nrhs, a, lda, ipiv, b, ldb, info);
		}

		public unsafe void Ssysv_(char* uplo, int* n, int* nrhs, float* a, int* lda, int* ipiv, float* b, int* ldb, float* work,
			int* lwork, int* info)
		{
			NativeOpenBlasLapackMethods.ssysv_(uplo, n, nrhs, a, lda, ipiv, b, ldb, work, lwork, info);
		}

		public unsafe void Sgetrf_(int* m, int* n, float* a, int* lda, int* ipiv, int* info)
		{
			NativeOpenBlasLapackMethods.sgetrf_(m, n, a, lda, ipiv, info);
		}

		public unsafe void Sgetri_(int* n, float* a, int* lda, int* ipiv, float* work, int* lwork, int* info)
		{
			NativeOpenBlasLapackMethods.sgetri_(n, a, lda, ipiv, work, lwork, info);
		}

		public unsafe void Dgesv(int* n, int* nrhs, double* a, int* lda, int* ipiv, double* b, int* ldb, int* info)
		{
			NativeOpenBlasLapackMethods.dgesv_(n, nrhs, a, lda, ipiv, b, ldb, info);
		}

		public unsafe void Dsysv_(char* uplo, int* n, int* nrhs, double* a, int* lda, int* ipiv, double* b, int* ldb, double* work,
			int* lwork, int* info)
		{
			NativeOpenBlasLapackMethods.dsysv_(uplo, n, nrhs, a, lda, ipiv, b, ldb, work, lwork, info);
		}

		public unsafe void Dgetrf_(int* m, int* n, double* a, int* lda, int* ipiv, int* info)
		{
			NativeOpenBlasLapackMethods.dgetrf_(m, n, a, lda, ipiv, info);
		}

		public unsafe void Dgetri_(int* n, double* a, int* lda, int* ipiv, double* work, int* lwork, int* info)
		{
			NativeOpenBlasLapackMethods.dgetri_(n, a, lda, ipiv, work, lwork, info);
		}

		/// <summary>
		/// The external native OpenBLAS LAPACK methods.
		/// </summary>
		internal static class NativeOpenBlasLapackMethods
		{
			static NativeOpenBlasLapackMethods()
			{
				PlatformDependentDllUtils.EnsureSetPlatformDependentDllDirectory();
			}

			[DllImport(dllName: "libopenblas", EntryPoint = "sgesv_")]
			internal static extern unsafe void sgesv_(int* n, int* nrhs, float* a, int* lda, int* ipiv, float* b, int* ldb, int* info);

			[DllImport(dllName: "libopenblas", EntryPoint = "ssysv_")]
			internal static extern unsafe void ssysv_(char* uplo, int* n, int* nrhs, float* a, int* lda, int* ipiv, float* b, int* ldb, float* work, int* lwork, int* info);

			[DllImport(dllName: "libopenblas", EntryPoint = "sgetrf_")]
			internal static extern unsafe void sgetrf_(int* m, int* n, float* a, int* lda, int* ipiv, int* info);

			[DllImport(dllName: "libopenblas", EntryPoint = "sgetri_")]
			internal static extern unsafe void sgetri_(int* n, float* a, int* lda, int* ipiv, float* work, int* lwork, int* info);

			[DllImport(dllName: "libopenblas", EntryPoint = "dgesv_")]
			internal static extern unsafe void dgesv_(int* n, int* nrhs, double* a, int* lda, int* ipiv, double* b, int* ldb, int* info);

			[DllImport(dllName: "libopenblas", EntryPoint = "dsysv_")]
			internal static extern unsafe void dsysv_(char* uplo, int* n, int* nrhs, double* a, int* lda, int* ipiv, double* b, int* ldb, double* work, int* lwork, int* info);

			[DllImport(dllName: "libopenblas", EntryPoint = "dgetrf_")]
			internal static extern unsafe void dgetrf_(int* m, int* n, double* a, int* lda, int* ipiv, int* info);

			[DllImport(dllName: "libopenblas", EntryPoint = "dgetri_")]
			internal static extern unsafe void dgetri_(int* n, double* a, int* lda, int* ipiv, double* work, int* lwork, int* info);
		}
	}
}
