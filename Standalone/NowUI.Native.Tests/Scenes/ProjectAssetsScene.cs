using System;
using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Native.TestScenes
{
    public sealed class ProjectAssetsScene : INowScene
    {
        readonly Texture2D texture = Resources.Load<Texture2D>("Assets/UI/tiles.png")
            ?? throw new InvalidOperationException("Missing tiles texture");
        readonly Sprite green = Resources.Load<Sprite>("Assets/UI/tiles.png#green")
            ?? throw new InvalidOperationException("Missing named sprite");

        public void Draw(NowRect view)
        {
            Now.Rectangle(view).SetColor(Color.black).Draw();
            Now.Rectangle(new NowRect(0, 0, 32, 32)).SetTexture(texture).Draw();
            Now.Rectangle(new NowRect(32, 0, 32, 32)).SetSprite(green).Draw();
        }
    }
}
