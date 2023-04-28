# About
Based on [`com.unity.splines`](https://docs.unity3d.com/Packages/com.unity.splines@2.2/manual/index.html) version 2.2.1.

The original package provides linear, cubic Bézier, and Catmull-Rom splines. They are not [C^2-continuous](https://www.youtube.com/watch?v=jvPPXbo87ds).

This fork provides the uniform cubic B-Splines (with 4 control points per curve).

# Installation
* Install the package [from its git URL](https://docs.unity3d.com/Manual/upm-ui-giturl.html) or [from a local copy](https://docs.unity3d.com/Manual/upm-ui-local.html).
* It does not depend on the `com.unity.splines` package and will not conflict with it if it's present

# Possible Improvements
* [Global curve interpolation](https://pages.mtu.edu/~shene/COURSES/cs3621/NOTES/INT-APP/CURVE-INT-global.html)
* Automatic conversion between cubic Bézier and cubic B-Splines
* Shared interfaces with `com.unity.splines`, less duplicated code
* Shader utility functions have not been reimplemented for B-Splines
* Automated tests have not been reimplemented for B-Splines
* Arbitrary degree and number of control points per curve? (https://xiaoxingchen.github.io/2020/03/02/bspline_in_so3/general_matrix_representation_for_bsplines.pdf)

# Implementation Details
The B-Splines are [clamped](https://pages.mtu.edu/%7Eshene/COURSES/cs3621/NOTES/spline/bspline-curve-prop.html) to guarantee that they pass through the first and last points.

The splines are calculated internally using their matrix forms. The multiplication of the control point vector by the basis function matrix is cached for every point, so that evaluation at any given point only entails a vector dot product.

Derivatives are analytical and re-use the same cached multiplication. Derivatives need only be applied to the parameter vector `[1, t, t^2, t^3]`. This vector becomes `[0, 1, 2t, 3t^2]` for the tangent and `[0, 0, 2, 6t]` for the acceleration.

The quadratic B-Spline was not implemented. It may look enticing by having one less control point, especially in 2D contexts, but it isn't C^2-continuous. Only the first `degree - 1` derivatives of B-Splines are continuous.
