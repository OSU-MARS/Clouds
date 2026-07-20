# palettes for coloring point clouds 
library(dplyr)
library(magrittr)
library(readr)
library(stringr)
library(tibble)

# Applied Imagery QT Modeler .qpl format
# R G B scaling alpha
to_qpl = function(hexPalette)
{
  qpl = tibble(name = hexPalette) %>%
    mutate(R = as.numeric(strtoi(str_sub(name, start = 2, end = 3), base = 16)),
           G = as.numeric(strtoi(str_sub(name, start = 4, end = 5), base = 16)),
           B = as.numeric(strtoi(str_sub(name, start = 6, end = 7), base = 16)),
           S = (row_number() - 1) / (n() - 1), # scaling
           A = as.numeric(strtoi(str_sub(name, start = 8, end = 9), base = 16))) %>% # alpha
    select(-name)
  qpl %<>% add_row(qpl %>% slice_head(), .before = 1) %>% add_row(qpl %>% slice_tail())
  qpl$S[1] = -0.1
  qpl$S[nrow(qpl)] = 1.1
  return(qpl)
}

viridisQpl = to_qpl(viridisLite::viridis(n = 256))
write_delim(viridisQpl, "viridis.qpl")
