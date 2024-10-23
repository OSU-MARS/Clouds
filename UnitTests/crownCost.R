# code for checking TreeCrownCostField
library(dplyr)
library(ggplot2)
library(readxl)

theme_set(theme_bw() + theme(axis.line = element_line(linewidth = 0.3), panel.border = element_blank()))

costField = read_xlsx(file.path(getwd(), "UnitTests/bin/Debug/costField.xlsx")) %>%
  mutate(x = 0.3048 * 1.5 * rep(0:62, 63), 
         y = 0.3048 * 1.5 * (62 - rep(0:62, each = 63)),
         value = na_if(0.3048 * as.numeric(value), Inf),
         valueBaseline = na_if(0.3048 * as.numeric(valueBaseline), Inf))

# range(costField$value / costField$radius, na.rm = TRUE) # consistency check at Voronoi simplification
print(costField %>% filter(y == 31), n = 63)

ggplot() +
  geom_raster(aes(x = x, y = y, fill = value), costField %>% filter()) +
  coord_equal(xlim = c(0, 19), ylim = c(0, 19)) +
  labs(x = "x, m", y = "y, m", fill = "path\ncost, m") +
  scale_fill_viridis_c()
