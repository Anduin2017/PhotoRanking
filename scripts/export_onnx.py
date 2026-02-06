import torch
import onnx
import os
from transformers import CLIPModel, CLIPProcessor

def export_clip():
    model_name = "openai/clip-vit-base-patch32"
    print(f"Loading model {model_name}...")
    try:
        model = CLIPModel.from_pretrained(model_name)
    except Exception as e:
        print(f"Failed to load model: {e}")
        print("Please ensure you have internet access and 'transformers' installed.")
        return

    model.eval()

    # We wrap the visual part to simple forward pass
    class ClipVisionWrapper(torch.nn.Module):
        def __init__(self, vision_model, visual_projection):
            super().__init__()
            self.vision_model = vision_model
            self.visual_projection = visual_projection
        
        def forward(self, pixel_values):
            vision_outputs = self.vision_model(pixel_values=pixel_values)
            pooled_output = vision_outputs.pooler_output
            image_features = self.visual_projection(pooled_output)
            # Normalize to match CLIP behavior
            image_features = image_features / image_features.norm(p=2, dim=-1, keepdim=True)
            return image_features

    wrapper = ClipVisionWrapper(model.vision_model, model.visual_projection)
    wrapper.eval()

    # Input: [Batch, 3, 224, 224]
    dummy_input = torch.randn(1, 3, 224, 224)
    
    output_dir = "../src/Anduin.PhotoRanking/models"
    os.makedirs(output_dir, exist_ok=True)
    onnx_path = os.path.join(output_dir, "clip-visual.onnx")

    print(f"Exporting to {onnx_path}...")
    torch.onnx.export(
        wrapper,
        dummy_input,
        onnx_path,
        opset_version=14,
        input_names=["pixel_values"],
        output_names=["image_features"],
        dynamic_axes={
            "pixel_values": {0: "batch_size"},
            "image_features": {0: "batch_size"}
        }
    )
    print("Export complete!")

if __name__ == "__main__":
    export_clip()
