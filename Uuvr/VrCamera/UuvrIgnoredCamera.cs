using UnityEngine;

namespace Uuvr.VrCamera;

// Marks a camera UUVR created itself, so the camera manager doesn't try to turn it into
// a VR camera and end up recursing into its own rendering.
//
// This is a component rather than an entry in a static collection on purpose: looking a
// component up is an engine call that always exists, while the generic collection methods
// it replaces are managed code that IL2CPP strips out of games that never call them.
public class UuvrIgnoredCamera : MonoBehaviour
{
#if CPP
    public UuvrIgnoredCamera(System.IntPtr pointer) : base(pointer)
    {
    }
#endif
}
