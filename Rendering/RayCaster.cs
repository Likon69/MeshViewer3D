using OpenTK.Mathematics;
using System;

namespace MeshViewer3D.Rendering
{
    /// <summary>
    /// Utilitaires de raycasting et picking 3D
    /// Permet de détecter la position sous le curseur dans le viewport
    /// </summary>
    public static class RayCaster
    {
        /// <summary>
        /// Convertit les coordonnées écran (pixels) en rayon 3D dans l'espace monde
        /// </summary>
        /// <param name="screenX">Position X à l'écran (0 = gauche)</param>
        /// <param name="screenY">Position Y à l'écran (0 = haut)</param>
        /// <param name="viewportWidth">Largeur du viewport</param>
        /// <param name="viewportHeight">Hauteur du viewport</param>
        /// <param name="viewMatrix">Matrice de vue de la caméra</param>
        /// <param name="projectionMatrix">Matrice de projection</param>
        /// <returns>Structure Ray avec origin et direction</returns>
        public static Ray ScreenToWorldRay(
            float screenX, float screenY,
            int viewportWidth, int viewportHeight,
            Matrix4 viewMatrix, Matrix4 projectionMatrix)
        {
            // Convertir coordonnées écran en NDC (Normalized Device Coordinates)
            // NDC: x et y entre -1 et 1, z entre -1 (near) et 1 (far)
            float ndcX = (2.0f * screenX) / viewportWidth - 1.0f;
            float ndcY = 1.0f - (2.0f * screenY) / viewportHeight;  // Inverser Y (écran vs OpenGL)
            
            // Point proche (near plane) et lointain (far plane) en NDC
            Vector4 rayClipNear = new Vector4(ndcX, ndcY, -1.0f, 1.0f);
            Vector4 rayClipFar = new Vector4(ndcX, ndcY, 1.0f, 1.0f);
            
            // Inverse des matrices pour passer de clip space à world space
            Matrix4 invProjection = Matrix4.Invert(projectionMatrix);
            Matrix4 invView = Matrix4.Invert(viewMatrix);
            
            // Passer de clip space à view space
            Vector4 rayEyeNear = invProjection * rayClipNear;
            Vector4 rayEyeFar = invProjection * rayClipFar;
            
            // Diviser par w (perspective divide)
            rayEyeNear /= rayEyeNear.W;
            rayEyeFar /= rayEyeFar.W;
            
            // Passer de view space à world space
            Vector4 rayWorldNear = invView * rayEyeNear;
            Vector4 rayWorldFar = invView * rayEyeFar;
            
            // Extraire les positions 3D
            Vector3 rayOrigin = new Vector3(rayWorldNear.X, rayWorldNear.Y, rayWorldNear.Z);
            Vector3 rayEnd = new Vector3(rayWorldFar.X, rayWorldFar.Y, rayWorldFar.Z);
            
            // Calculer la direction normalisée
            Vector3 rayDirection = Vector3.Normalize(rayEnd - rayOrigin);
            
            return new Ray(rayOrigin, rayDirection);
        }
        
        /// <summary>
        /// Teste l'intersection entre un rayon et un triangle
        /// Algorithme de Möller-Trumbore - VERSION DOUBLE-SIDED
        /// Teste les deux faces du triangle (front et back)
        /// </summary>
        public static bool RayTriangleIntersect(
            Ray ray,
            Vector3 v0, Vector3 v1, Vector3 v2,
            out float distance,
            out Vector3 hitPoint)
        {
            distance = 0;
            hitPoint = Vector3.Zero;
            
            const float EPSILON = 0.0000001f;
            
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.Direction, edge2);
            float a = Vector3.Dot(edge1, h);
            
            // Rayon parallèle au triangle (a proche de 0)
            if (Math.Abs(a) < EPSILON)
                return false;
            
            float f = 1.0f / a;
            Vector3 s = ray.Origin - v0;
            float u = f * Vector3.Dot(s, h);
            
            if (u < 0.0f || u > 1.0f)
                return false;
            
            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(ray.Direction, q);
            
            if (v < 0.0f || u + v > 1.0f)
                return false;
            
            // Calculer t (distance le long du rayon)
            float t = f * Vector3.Dot(edge2, q);
            
            // Pour double-sided: accepter t positif (intersection devant la caméra)
            if (t > 0.001f) // Petit seuil pour éviter self-intersection
            {
                distance = t;
                hitPoint = ray.Origin + ray.Direction * t;
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Teste l'intersection entre un rayon et un plan horizontal (Y constant)
        /// Utile pour placer des objets sur un plan de référence
        /// </summary>
        public static bool RayPlaneIntersect(
            Ray ray,
            float planeY,
            out Vector3 hitPoint)
        {
            hitPoint = Vector3.Zero;
            
            // Si le rayon est parallèle au plan (direction.Y proche de 0)
            if (Math.Abs(ray.Direction.Y) < 0.0001f)
                return false;
            
            // Calculer t (distance le long du rayon)
            float t = (planeY - ray.Origin.Y) / ray.Direction.Y;
            
            // L'intersection doit être devant la caméra
            if (t < 0)
                return false;
            
            hitPoint = ray.Origin + ray.Direction * t;
            return true;
        }
        
        /// <summary>
        /// Teste l'intersection rayon-cylindre (pour sélectionner des blackspots)
        /// </summary>
        public static bool RayCylinderIntersect(
            Ray ray,
            Vector3 cylinderCenter,
            float radius,
            float height,
            out float distance)
        {
            distance = float.MaxValue;
            
            // Test contre cylindre infini (ignorer Y)
            Vector3 centerToRay = ray.Origin - cylinderCenter;
            Vector2 centerToRayXZ = new Vector2(centerToRay.X, centerToRay.Z);
            Vector2 rayDirXZ = new Vector2(ray.Direction.X, ray.Direction.Z);
            
            float a = rayDirXZ.X * rayDirXZ.X + rayDirXZ.Y * rayDirXZ.Y;
            float b = 2.0f * (centerToRayXZ.X * rayDirXZ.X + centerToRayXZ.Y * rayDirXZ.Y);
            float c = centerToRayXZ.X * centerToRayXZ.X + centerToRayXZ.Y * centerToRayXZ.Y - radius * radius;
            
            float discriminant = b * b - 4 * a * c;
            
            if (discriminant < 0)
                return false;
            
            float sqrtDisc = (float)Math.Sqrt(discriminant);
            float t1 = (-b - sqrtDisc) / (2 * a);
            float t2 = (-b + sqrtDisc) / (2 * a);
            
            // Tester contre les caps du cylindre
            float minY = cylinderCenter.Y;
            float maxY = cylinderCenter.Y + height;
            
            bool hit = false;
            
            if (t1 > 0)
            {
                Vector3 p1 = ray.GetPoint(t1);
                if (p1.Y >= minY && p1.Y <= maxY)
                {
                    distance = t1;
                    hit = true;
                }
            }
            
            if (t2 > 0 && t2 < distance)
            {
                Vector3 p2 = ray.GetPoint(t2);
                if (p2.Y >= minY && p2.Y <= maxY)
                {
                    distance = t2;
                    hit = true;
                }
            }
            
            return hit;
        }
    }
    
    /// <summary>
    /// Structure représentant un rayon 3D
    /// </summary>
    public struct Ray
    {
        public Vector3 Origin;
        public Vector3 Direction;  // Doit être normalisé
        
        public Ray(Vector3 origin, Vector3 direction)
        {
            Origin = origin;
            Direction = direction;
        }
        
        /// <summary>
        /// Calcule un point le long du rayon
        /// </summary>
        public Vector3 GetPoint(float distance)
        {
            return Origin + Direction * distance;
        }
    }
}
