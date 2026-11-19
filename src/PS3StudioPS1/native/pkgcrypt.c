/*
 * Bulk block transform for the bundled PSL1GHT package writers. Each
 * 16-byte block uses SHA-1 of an independent 64-byte counter context.
 * The input and returned counter match the ps3py pkgcrypt interface.
 */
#define PY_SSIZE_T_CLEAN
#define Py_LIMITED_API 0x030a0000
#define WIN32_LEAN_AND_MEAN
#include <Python.h>
#include <windows.h>
#include <stdint.h>
#include <string.h>

/* Exported by both bundled OpenSSL 1.1 runtimes. A caller-owned digest
 * buffer makes the one-shot API safe to call from multiple workers. */
__declspec(dllimport) unsigned char *SHA1(const unsigned char *, size_t, unsigned char *);

enum { BLOCK = 16, CONTEXT_BYTES = 64, MAX_WORKERS = 4, MIN_WORKER_BYTES = 512 * 1024 };

typedef struct {
    unsigned char context[CONTEXT_BYTES];
    uint64_t first_counter;
    const unsigned char *input;
    unsigned char *output;
    size_t length;
} BlockRange;

static void put_counter(unsigned char *context, uint64_t value)
{
    for (int i = CONTEXT_BYTES - 1; i >= CONTEXT_BYTES - 8; --i) {
        context[i] = (unsigned char)value;
        value >>= 8;
    }
}

static void transform(BlockRange *range)
{
    unsigned char digest[20];
    uint64_t counter = range->first_counter;
    for (size_t offset = 0; offset < range->length; offset += BLOCK) {
        put_counter(range->context, counter++);
        SHA1(range->context, CONTEXT_BYTES, digest);
        size_t count = range->length - offset;
        if (count > BLOCK) count = BLOCK;
        for (size_t i = 0; i < count; ++i)
            range->output[offset+i] = range->input[offset+i] ^ digest[i];
    }
}

static void CALLBACK transform_work(PTP_CALLBACK_INSTANCE instance, void *context, PTP_WORK work)
{
    (void)instance; (void)work;
    transform(context);
}

static PyObject *pkgcrypt(PyObject *self, PyObject *args)
{
    (void)self;
    PyObject *context_bytes, *input_bytes;
    Py_ssize_t length;
    if (!PyArg_ParseTuple(args, "SSn", &context_bytes, &input_bytes, &length))
        return NULL;
    if (PyBytes_Size(context_bytes) != CONTEXT_BYTES || length < 0 || length > PyBytes_Size(input_bytes)) {
        PyErr_SetString(PyExc_ValueError, "Invalid package context or input length");
        return NULL;
    }
    PyObject *encoded = PyBytes_FromStringAndSize(NULL, length);
    if (!encoded) return NULL;
    const unsigned char *context = (const unsigned char *)PyBytes_AsString(context_bytes);
    const unsigned char *data = (const unsigned char *)PyBytes_AsString(input_bytes);
    unsigned char *output = (unsigned char *)PyBytes_AsString(encoded);
    uint64_t initial = 0;
    for (int i = CONTEXT_BYTES-8; i < CONTEXT_BYTES; ++i) initial = (initial << 8) | context[i];
    size_t size = (size_t)length;
    size_t blocks = (size + BLOCK-1) / BLOCK;
    size_t workers = size / MIN_WORKER_BYTES;
    if (workers > MAX_WORKERS) workers = MAX_WORKERS;
    if (workers > 1) {
        DWORD cpus = GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
        if (workers > cpus) workers = cpus;
    }
    if (!workers) workers = 1;
    BlockRange ranges[MAX_WORKERS];
    PTP_WORK work[MAX_WORKERS] = {0};
    Py_BEGIN_ALLOW_THREADS
    for (size_t i = 0; i < workers; ++i) {
        size_t first = blocks * i / workers;
        size_t end = blocks * (i+1) / workers * BLOCK;
        if (end > size) end = size;
        BlockRange *r = &ranges[i];
        memcpy(r->context, context, CONTEXT_BYTES);
        r->first_counter = initial + first;
        r->input = data + first * BLOCK;
        r->output = output + first * BLOCK;
        r->length = end - first * BLOCK;
        if (i+1 < workers) work[i] = CreateThreadpoolWork(transform_work, r, NULL);
        if (work[i]) SubmitThreadpoolWork(work[i]);
        else transform(r);
    }
    for (size_t i = 0; i+1 < workers; ++i) {
        if (!work[i]) continue;
        WaitForThreadpoolWorkCallbacks(work[i], FALSE);
        CloseThreadpoolWork(work[i]);
    }
    Py_END_ALLOW_THREADS
    unsigned char next[CONTEXT_BYTES];
    memcpy(next, context, CONTEXT_BYTES);
    put_counter(next, initial + blocks);
    PyObject *next_bytes = PyBytes_FromStringAndSize((const char *)next, CONTEXT_BYTES);
    if (!next_bytes) { Py_DECREF(encoded); return NULL; }
    return Py_BuildValue("NN", encoded, next_bytes);
}

/* The two pinned writer versions register their SHA-1 wrapper at startup.
 * The identical primitive above is supplied by their existing OpenSSL DLL. */
static PyObject *register_sha1(PyObject *self, PyObject *callback)
{
    (void)self;
    if (!PyCallable_Check(callback)) {
        PyErr_SetString(PyExc_TypeError, "Expected a SHA-1 function");
        return NULL;
    }
    Py_RETURN_NONE;
}

static PyMethodDef methods[] = {
    {"pkgcrypt", pkgcrypt, METH_VARARGS, "Transform package bytes and advance the counter."},
    {"register_sha1_callback", register_sha1, METH_O, "Initialize the package SHA-1 transform."},
    {NULL, NULL, 0, NULL}
};
static struct PyModuleDef module = {PyModuleDef_HEAD_INIT, "pkgcrypt", NULL, -1, methods};
PyMODINIT_FUNC PyInit_pkgcrypt(void) { return PyModule_Create(&module); }
