# code for checking TreeCrownCostField
library(dplyr)
library(ggplot2)
library(readxl)

theme_set(theme_bw() + theme(axis.line = element_line(linewidth = 0.3), panel.border = element_blank()))

costField = read_xlsx(file.path(getwd(), "UnitTests/bin/Debug/costField.xlsx")) %>%
  mutate(x = rep(0:62, 63), 
         y = rep(0:62, each = 63),
         value = na_if(as.numeric(value), Inf))

# range(costField$value / costField$radius, na.rm = TRUE) # consistency check at Voronoi simplification
print(costField %>% filter(y == 31), n = 63)

ggplot() +
  geom_raster(aes(x = x, y = y, fill = value), costField) +
  labs(x = "cost field cell index, x", y = "cost field cell index, y", fill = "IFT cost") +
  scale_fill_viridis_c() +
  scale_y_reverse()
